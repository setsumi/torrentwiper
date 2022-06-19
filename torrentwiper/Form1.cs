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
        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        int fileNum = 0;
        Dictionary<string, Int64> fileList = new Dictionary<string, Int64>();
        string torrentFolder = "";
        string torrentName = "";
        Int64 torrentSize = 0;
        Int64 deleteSize = 0;

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Open Torrent File";
            dialog.Filter = "Torrent files (*.torrent)|*.torrent";
            if (dialog.ShowDialog() != DialogResult.OK) return;

            torrentName = dialog.FileName;
            torrentSize = 0;
            fileList.Clear();

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
            }
            add_info(fileNum.ToString() + " file(s), " + FormatSize(torrentSize));
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (fileNum == 0) // .torrent is not loaded
            {
                MessageBox.Show(".torrent file is not loaded");
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = torrentFolder;
                DialogResult result = fbd.ShowDialog();
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    torrentFolder = fbd.SelectedPath;
                    deleteSize = 0;

                    textBox2.Text = torrentFolder;
                    listBoxFiles.Items.Clear();

                    string[] filenames = Directory.GetFiles(torrentFolder, "*", SearchOption.AllDirectories);
                    foreach (string file in filenames)
                    {
                        try
                        {
                            Int64 dummy = fileList[file.Substring(torrentFolder.Length)];
                        }
                        catch (KeyNotFoundException)
                        {
                            add_file(file);
                            FileInfo fi = new FileInfo(file);
                            deleteSize += fi.Length;
                        }
                    }
                    textBox3.Text = listBoxFiles.Items.Count.ToString() + " junk file(s) found, " + FormatSize(deleteSize);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (fileNum > 0 && listBoxFiles.Items.Count > 0)
            {
                DialogResult result = MessageBox.Show("Please confirm files removal", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes)
                {
                    foreach (string file in listBoxFiles.Items)
                    {
                    RETRY_DELETE:
                        try
                        {
                            FileInfo fileInfo = new FileInfo(file);
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
                                goto RETRY_DELETE;
                            }
                        }
                    }
                    listBoxFiles.Items.Clear();
                    textBox3.Text = "Junk file(s) removed";
                }
            }
        }

        static string FormatSize(Int64 value, int decimalPlaces = 1)
        {
            if (value < 0) { return "-" + FormatSize(-value); }

            int i = 0;
            decimal dValue = (decimal)value;
            while (Math.Round(dValue, decimalPlaces) >= 1000)
            {
                dValue /= 1024;
                i++;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", dValue, SizeSuffixes[i]);
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
    }
}
