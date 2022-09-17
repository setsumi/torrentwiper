using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

using BencodeNET;
using BencodeNET.Objects;

namespace torrentwiper
{
    public partial class Form1 : Form
    {
        int fileNum = 0;
        Dictionary<string, Int64> fileList = new Dictionary<string, Int64>();
        Dictionary<string, Int64> dirList = new Dictionary<string, Int64>();
        string torrentFolder = "";
        string torrentName = "";
        Int64 torrentSize = 0;
        Int64 deleteSize = 0;

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

            torrentName = dialog.FileName;
            torrentSize = 0;
            fileList.Clear();
            dirList.Clear();

            listBoxInfo.Items.Clear();
            textBox1.Text = torrentName;

            TorrentFile torrent = Bencode.DecodeTorrentFile(torrentName);

            add_info(torrent.Info["name"].ToString());
            if (torrent.Info["source"] != null)
                add_info(torrent.Info["source"].ToString());
            add_info(torrent.Comment);
            add_info(torrent.CreatedBy);
            add_info(torrent.CreationDate);

            fileNum = 0;
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
                fileNum++;
                Int64 sz = Int64.Parse(file["length"].ToString());
                torrentSize += sz;
                fileList.Add(spath, sz);
                // add torrent's file dir
                try
                {
                    dirList.Add(Path.GetDirectoryName(spath), 0);
                }
                catch (ArgumentException) { }
            }
            add_info(fileNum.ToString() + " file(s), " + FormatSize(torrentSize));
        }

        private void btnOpenFolder_Click(object sender, EventArgs e)
        {
            if (fileNum == 0) // .torrent is not loaded
            {
                MessageBox.Show(".torrent file is not loaded");
                return;
            }

            var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = torrentFolder;
            DialogResult result = fbd.ShowDialog();
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                torrentFolder = fbd.SelectedPath;
                deleteSize = 0;

                textBox2.Text = torrentFolder;
                listBoxFiles.Items.Clear();
                listBoxDirs.Items.Clear();

                int totalFiles = 0, totalFolders = 0;
                Int64 totalSize = 0;

                // find junk files
                string[] filenames = Directory.GetFiles(torrentFolder, "*", SearchOption.AllDirectories);
                foreach (string file in filenames)
                {
                    totalFiles++;
                    var fi = new FileInfo(file);
                    totalSize += fi.Length;
                    string tf = file.Substring(torrentFolder.Length);
                    try
                    {
                        Int64 dummy = fileList[tf];
                    }
                    catch (KeyNotFoundException)
                    {
                        add_file(tf);
                        deleteSize += fi.Length;
                    }
                }

                // find junk folders
                var dirs = new Dictionary<string, Int64>();
                string[] directories = Directory.GetDirectories(torrentFolder, "*", SearchOption.AllDirectories);
                foreach (string d in directories)
                {
                    totalFolders++;
                    string dir = d.Substring(torrentFolder.Length);
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

                textBox3.Text = "Junk found: " + listBoxFiles.Items.Count + " files (" + FormatSize(deleteSize) +
                    "), " + listBoxDirs.Items.Count + " folders | Total folder stats: " + totalFiles +
                    " files (" + FormatSize(totalSize) + "), " + totalFolders + " folders.";
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (fileNum > 0 && (listBoxFiles.Items.Count > 0 || listBoxDirs.Items.Count > 0))
            {
                DialogResult result = MessageBox.Show("Confirm junk removal", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes)
                {
                    // delete files
                    foreach (string tf in listBoxFiles.Items)
                    {
                        string file = torrentFolder + tf;
                    RETRY_DELETE_FILE:
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo.IsReadOnly)
                                fileInfo.IsReadOnly = false;
                            fileInfo.Delete();
                        }
                        catch (Exception ex)
                        {
                            var res = MessageBox.Show(ex.Message, "Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error);
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
                    foreach (string td in listBoxDirs.Items.Cast<string>().AsEnumerable().Reverse())
                    {
                        string dir = torrentFolder + td;
                    RETRY_DELETE_DIR:
                        try
                        {
                            Directory.Delete(dir);
                        }
                        catch (Exception ex)
                        {
                            var res = MessageBox.Show(ex.Message, "Error", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error);
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

                    listBoxFiles.Items.Clear();
                    listBoxDirs.Items.Clear();
                    textBox3.Text = "Junk removed.";
                }
            }
        }

        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string FormatSize(Int64 value, int decimalPlaces = 2)
        {
            if (value < 0) { return "-" + FormatSize(-value, decimalPlaces); }

            int i = 0;
            decimal dValue = (decimal)value;
            while (Math.Round(dValue, decimalPlaces) >= 1024)
            {
                dValue /= 1024;
                i++;
            }

            return i == 0 ? string.Format("{0} {1}", value, SizeSuffixes[i]) :
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
            if (dirList.ContainsKey(dir)) return true;

            foreach (string d in dirList.Keys)
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

        // path without last \
        //private string get_parent(string path)
        //{
        //    int i = path.LastIndexOf('\\');
        //    return i > 0 ? path.Substring(0, i) : "";
        //}
    }
}
