using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.Threading.Tasks;
using BencodeNET;
using BencodeNET.Objects;

namespace torrentwiper
{
    public partial class Form1 : Form
    {
        int _fileNum = 0;
        readonly Dictionary<string, Int64> _fileList = new Dictionary<string, Int64>();
        readonly Dictionary<string, Int64> _dirList = new Dictionary<string, Int64>();
        string _torrentFolder = "";
        string _torrentName = "";
        Int64 _torrentSize = 0;
        Int64 _deleteSize = 0;

        public Form1()
        {
            InitializeComponent();

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            this.Text += " " + fvi.FileVersion;
        }

        private void btnOpenTorrent_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Open Torrent File";
            dialog.Filter = "Torrent files (*.torrent)|*.torrent";
            if (dialog.ShowDialog() != DialogResult.OK) return;

            _torrentName = dialog.FileName;
            _torrentSize = 0;
            _fileList.Clear();
            _dirList.Clear();

            listBoxInfo.Items.Clear();
            textBox1.Text = _torrentName;

            TorrentFile torrent = Bencode.DecodeTorrentFile(_torrentName);

            add_info(torrent.Info["name"].ToString());
            if (torrent.Info["source"] != null)
                add_info(torrent.Info["source"].ToString());
            add_info(torrent.Comment);
            add_info(torrent.CreatedBy);
            add_info(torrent.CreationDate);

            _fileNum = 0;
            BList files = (BList)torrent.Info["files"];
            foreach (BDictionary file in files)
            {
                //add_info(file["length"].ToString());

                BList path = (BList)file["path"];
                string spath = "";
                foreach (BString elem in path)
                {
                    spath += "\\" + elem;
                }
                _fileNum++;
                Int64 sz = Int64.Parse(file["length"].ToString());
                _torrentSize += sz;
                _fileList.Add(spath, sz);
                // add torrent's file dir
                try
                {
                    _dirList.Add(Path.GetDirectoryName(spath), 0);
                }
                catch (ArgumentException) { }
            }
            add_info(_fileNum.ToString() + " file(s), " + FormatSize(_torrentSize));
        }

        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            if (_fileNum == 0) // .torrent is not loaded
            {
                MessageBox.Show(".torrent file is not loaded");
                return;
            }

            var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = _torrentFolder;
            DialogResult result = fbd.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                _torrentFolder = fbd.SelectedPath;
                _deleteSize = 0;

                on_torrent_ui_reset(_torrentFolder);
                this.Refresh();

                int totalFiles = 0, totalFolders = 0;
                Int64 totalSize = 0;

                // find junk files
                string[] filenames = Directory.GetFiles(_torrentFolder, "*", SearchOption.AllDirectories);
                foreach (string file in filenames)
                {
                    totalFiles++;
                    var fi = new FileInfo(file);
                    totalSize += fi.Length;
                    string tf = file.Substring(_torrentFolder.Length);
                    try
                    {
                        Int64 dummy = _fileList[tf];
                    }
                    catch (KeyNotFoundException)
                    {
                        add_file(tf);
                        _deleteSize += fi.Length;
                    }
                }

                // find junk folders
                var dirs = new Dictionary<string, Int64>();
                string[] directories = Directory.GetDirectories(_torrentFolder, "*", SearchOption.AllDirectories);
                foreach (string d in directories)
                {
                    totalFolders++;
                    string dir = d.Substring(_torrentFolder.Length);
                    if (dir != "\\" && !is_subdir(dir))
                    {
                        try
                        {
                            dirs.Add(dir, 0);
                        }
                        catch (ArgumentException) { }
                    }
                }
                foreach (string d in dirs.Keys)
                {
                    listBoxDirs.Items.Add(d);
                }

                textBox3.Text = "Junk found: " + listBoxFiles.Items.Count + " files (" + FormatSize(_deleteSize) +
                    "), " + listBoxDirs.Items.Count + " folders | Total folder stats: " + totalFiles +
                    " files (" + FormatSize(totalSize) + "), " + totalFolders + " folders.";

                if (listBoxFiles.Items.Count == 0 && listBoxDirs.Items.Count == 0)
                {
                    label1.Text = "Folder is clean";
                    label1.BackColor = Color.LightGreen;
                }
            }
        }

        private async void btnDelete_Click(object sender, EventArgs e)
        {
            if (_fileNum > 0 && (listBoxFiles.Items.Count > 0 || listBoxDirs.Items.Count > 0))
            {
                DialogResult result = MessageBox.Show("Confirm junk removal", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes)
                {
                    await Task.Run(() => DeleteJunk());
                }
            }
        }

        private void DeleteJunk()
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            string torrFolder = null;
            string[] files = null;
            string[] dirs = null;
            this.Invoke((Action)(() =>
            {
                btnDelete.Enabled = false;
                btnOpenFolder.Enabled = false;
                btnOpenTorrent.Enabled = false;

                torrFolder = _torrentFolder;
                files = listBoxFiles.Items.Cast<string>().AsEnumerable().ToArray();
                dirs = listBoxDirs.Items.Cast<string>().AsEnumerable().Reverse().ToArray();
            }));
            int filesNum = files.Length;
            int dirsNum = dirs.Length;

            stopwatch.Start();
            DisplayDeleteProgress(this, filesNum, dirsNum);

            // delete files
            foreach (string tf in files)
            {
                string file = torrFolder + tf;
            RETRY_DELETE_FILE:
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.IsReadOnly)
                        fileInfo.IsReadOnly = false;
                    fileInfo.Delete();
                    filesNum--;
                    if (stopwatch.ElapsedMilliseconds > 500)
                    {
                        stopwatch.Restart();
                        DisplayDeleteProgress(this, filesNum, dirsNum);
                    }
                }
                catch (Exception ex)
                {
                    var res = DialogResult.Retry;
                    this.Invoke((Action)(() => res = MessageBox.Show(ex.Message, "Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error)));
                    if (res == DialogResult.Abort)
                    {
                        break;
                    }
                    else if (res == DialogResult.Retry)
                    {
                        goto RETRY_DELETE_FILE;
                    }
                }
            }

            // delete directories
            foreach (string td in dirs)
            {
                string dir = torrFolder + td;
            RETRY_DELETE_DIR:
                try
                {
                    Directory.Delete(dir);
                    dirsNum--;
                    if (stopwatch.ElapsedMilliseconds > 500)
                    {
                        stopwatch.Restart();
                        DisplayDeleteProgress(this, filesNum, dirsNum);
                    }
                }
                catch (Exception ex)
                {
                    var res = DialogResult.Retry;
                    this.Invoke((Action)(() => res = MessageBox.Show(ex.Message, "Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error)));
                    if (res == DialogResult.Abort)
                    {
                        break;
                    }
                    else if (res == DialogResult.Retry)
                    {
                        goto RETRY_DELETE_DIR;
                    }
                }
            }

            stopwatch.Stop();
            DisplayDeleteProgress(this, filesNum, dirsNum, (filesNum > 0 || dirsNum > 0) ? "DONE WITH LEFTOVERS" : "DONE CLEAN");
            this.Invoke((Action)(() =>
            {
                listBoxFiles.Items.Clear();
                listBoxDirs.Items.Clear();

                btnDelete.Enabled = true;
                btnOpenFolder.Enabled = true;
                btnOpenTorrent.Enabled = true;
            }));
        }

        static void DisplayDeleteProgress(Form1 form, int files, int dirs, string suffix = "")
        {
            form.Invoke((Action)(() => form.textBox3.Text = $"Deleting junk: {files} files, {dirs} folders... {suffix}"));
        }

        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string FormatSize(Int64 value, int decimalPlaces = 2)
        {
            if (value < 0) { return "-" + FormatSize(-value, decimalPlaces); }

            int i = 0;
            decimal dValue = (decimal)value;
            while (Math.Round(dValue, decimalPlaces) >= 800)
            {
                dValue /= 1024;
                i++;
            }

            return value < 1024 ? string.Format("{0} {1}", value, SizeSuffixes[0]) :
                string.Format("{0:n" + decimalPlaces + "} {1}", dValue, SizeSuffixes[i]);
        }

        private void add_info(string str)
        {
            if (str != null) listBoxInfo.Items.Add(str);
        }

        private void add_info(DateTime dt)
        {
            if (dt != null && dt != default(DateTime)) listBoxInfo.Items.Add(dt.ToString());
        }

        private void add_file(string str)
        {
            if (str != null) listBoxFiles.Items.Add(str);
        }

        private bool is_subdir(string dir)
        {
            if (_dirList.ContainsKey(dir)) return true;

            foreach (string d in _dirList.Keys)
            {
                if (d.Length > dir.Length)
                {
                    string s = d.Substring(0, dir.Length);
                    if (d[dir.Length] == '\\' && s == dir)
                        return true;
                }
            }
            return false;
        }

        private void on_torrent_ui_reset(string folder = null)
        {
            textBox2.Text = string.IsNullOrEmpty(folder) ? "" : folder;
            label1.Text = "Not included in torrent";
            label1.BackColor = SystemColors.Control;
            listBoxFiles.Items.Clear();
            listBoxDirs.Items.Clear();
            textBox3.Text = "";
        }

        // path without last \
        //private string get_parent(string path)
        //{
        //    int i = path.LastIndexOf('\\');
        //    return i > 0 ? path.Substring(0, i) : "";
        //}
    }
}
