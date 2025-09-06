using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace MirrorAudio
{
    // 依赖类型说明（都在 Program.cs 内定义）：
    // AppSettings, ShareModeOption, SyncModeOption, StatusSnapshot, 以及全局 _cfg（包含 ForcePassthrough/ForceRaw/ShowAdvanced）

    sealed class SettingsForm : Form
    {
        // 左侧状态控件
        readonly Label lblRun=new Label(), lblInput=new Label(), lblMain=new Label(), lblAux=new Label(),
                       lblMainFmt=new Label(), lblAuxFmt=new Label(), lblMainBuf=new Label(), lblAuxBuf=new Label(),
                       lblMainPer=new Label(), lblAuxPer=new Label();
        readonly Label lblMainReq=new Label(), lblMainQtz=new Label(), lblAuxReq=new Label(), lblAuxQtz=new Label();
        readonly Label lblMainPass=new Label(), lblAuxPass=new Label();

        // 右侧设置控件
        readonly ComboBox cmbInput=new ComboBox(), cmbMain=new ComboBox(), cmbAux=new ComboBox(),
                          cmbShareMain=new ComboBox(), cmbSyncMain=new ComboBox(),
                          cmbShareAux=new ComboBox(),  cmbSyncAux=new ComboBox();

        readonly NumericUpDown numRateMain=new NumericUpDown(), numBitsMain=new NumericUpDown(), numBufMain=new NumericUpDown(),
                               numRateAux =new NumericUpDown(), numBitsAux =new NumericUpDown(), numBufAux =new NumericUpDown();

        readonly CheckBox chkAutoStart=new CheckBox(), chkLogging=new CheckBox();

        // —— 本次修复：把三个复选框升级为“字段”，以便 SaveAndClose 能访问 —— //
        readonly CheckBox chkForcePass=new CheckBox();
        readonly CheckBox chkRaw=new CheckBox();
        readonly CheckBox chkAdvanced=new CheckBox();

        readonly Button btnOk=new Button(), btnCancel=new Button(), btnRefresh=new Button(), btnCopy=new Button(), btnReload=new Button();

        readonly Func<StatusSnapshot> _statusProvider;

        sealed class DevItem { public string Id,Name; public override string ToString() => Name; }

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings cur, Func<StatusSnapshot> statusProvider)
        {
            _statusProvider = statusProvider ?? (() => new StatusSnapshot { Running = false });

            Text = "MirrorAudio 设置";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            MinimumSize = new Size(980, 620);
            Size = new Size(1100, 680);

            // —— 左右各 1/2 —— //
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.None,
                SplitterWidth = 6
            };
            Controls.Add(split);
            EventHandler keepHalf = (s, e) => { if (split.Width > 0) split.SplitterDistance = split.Width / 2; };
            Shown += keepHalf; Resize += keepHalf;

            // 左：状态区（滚动 + 紧凑表格）
            var left = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
            var grpS = new GroupBox { Text = "当前状态（打开查看，关闭即释放内存）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            var tblS = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
            tblS.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            tblS.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            AddRow(tblS, "运行状态", lblRun);
            AddRow(tblS, "输入", lblInput);
            AddRow(tblS, "主通道", lblMain);
            AddRow(tblS, "主格式", lblMainFmt);
            AddRow(tblS, "主缓冲", lblMainBuf);
            AddRow(tblS, "主周期", lblMainPer);
            AddRow(tblS, "主直通", lblMainPass);
            AddRow(tblS, "主缓冲(请求)", lblMainReq);
            AddRow(tblS, "主缓冲(量化)", lblMainQtz);
            AddRow(tblS, "副通道", lblAux);
            AddRow(tblS, "副格式", lblAuxFmt);
            AddRow(tblS, "副缓冲", lblAuxBuf);
            AddRow(tblS, "副周期", lblAuxPer);
            AddRow(tblS, "副直通", lblAuxPass);
            AddRow(tblS, "副缓冲(请求)", lblAuxReq);
            AddRow(tblS, "副缓冲(量化)", lblAuxQtz);

            var pBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 4) };
            btnRefresh.Text = "刷新状态";
            btnCopy.Text = "复制状态";
            btnRefresh.Click += (s, e) => RenderStatus();
            btnCopy.Click += (s, e) =>
            {
                Clipboard.SetText(BuildStatusText());
                MessageBox.Show("状态已复制。", "MirrorAudio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            // —— 三个复选框（字段） —— //
            chkForcePass.Text = "强制直通（能直通就不用重采样）";
            chkForcePass.AutoSize = true;
            chkRaw.Text = "RAW 优先（尽可能绕 APO）";
            chkRaw.AutoSize = true;
            chkAdvanced.Text = "显示高级状态";
            chkAdvanced.AutoSize = true;

            // 读取全局 _cfg 的默认值
            chkForcePass.Checked = _cfg.ForcePassthrough;
            chkRaw.Checked = _cfg.ForceRaw;
            chkAdvanced.Checked = _cfg.ShowAdvanced;

            chkAdvanced.CheckedChanged += (s,e) => {
                bool v = chkAdvanced.Checked;
                lblMainReq.Visible = lblMainQtz.Visible = lblAuxReq.Visible = lblAuxQtz.Visible = v;
            };

            pBtns.Controls.Add(btnRefresh);
            pBtns.Controls.Add(btnCopy);
            pBtns.Controls.Add(chkForcePass);
            pBtns.Controls.Add(chkRaw);
            pBtns.Controls.Add(chkAdvanced);

            grpS.Controls.Add(tblS);
            grpS.Controls.Add(pBtns);
            left.Controls.Add(grpS);
            split.Panel1.Controls.Add(left);

            // 右：设置（依次：设备 → 主输出 → 副输出 → 其他）
            var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

            // 4) 其他
            var gOpt = new GroupBox { Text = "其他", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            var pOpt = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Top, AutoSize = true };
            chkAutoStart.Text = "Windows 自启动";
            chkLogging.Text   = "启用日志（排障时开启）";
            pOpt.Controls.Add(chkAutoStart);
            pOpt.Controls.Add(chkLogging);
            gOpt.Controls.Add(pOpt);
            right.Controls.Add(gOpt);

            split.Panel2.Controls.Add(right);

            // 3) 副输出（直播推流）
            var gAux = new GroupBox { Text = "副输出（直播推流）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            var tAux = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
            tAux.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            tAux.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

            cmbShareAux.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSyncAux .DropDownStyle = ComboBoxStyle.DropDownList;
            cmbShareAux.Items.AddRange(new object[] { "自动（优先独占）", "强制独占", "强制共享" });
            cmbSyncAux .Items.AddRange(new object[] { "自动（事件优先）", "强制事件", "强制轮询" });

            numRateAux.Maximum = 384000;  numRateAux.Minimum = 44100;   numRateAux.Increment = 1000;
            numBitsAux.Maximum = 32;      numBitsAux.Minimum = 16;      numBitsAux.Increment = 8;
            numBufAux.Maximum  = 400;     numBufAux.Minimum  = 50;

            AddRow(tAux, "模式",                cmbShareAux);
            AddRow(tAux, "同步方式",            cmbSyncAux);
            AddRow(tAux, "采样率 (Hz，仅独占)", numRateAux);
            AddRow(tAux, "位深 (bit，仅独占)",  numBitsAux);
            AddRow(tAux, "缓冲 (ms)",            numBufAux);

            gAux.Controls.Add(tAux);
            right.Controls.Add(gAux);

            // 2) 主输出（高音质，低延迟）
            var gMain = new GroupBox { Text = "主输出（高音质，低延迟）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            var tMain = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
            tMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            tMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

            cmbShareMain.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSyncMain .DropDownStyle = ComboBoxStyle.DropDownList;
            cmbShareMain.Items.AddRange(new object[] { "自动（优先独占）", "强制独占", "强制共享" });
            cmbSyncMain .Items.AddRange(new object[] { "自动（事件优先）", "强制事件", "强制轮询" });

            numRateMain.Maximum = 384000; numRateMain.Minimum = 44100;  numRateMain.Increment = 1000;
            numBitsMain.Maximum = 32;     numBitsMain.Minimum = 16;     numBitsMain.Increment = 8;
            numBufMain.Maximum  = 200;    numBufMain.Minimum  = 4;

            AddRow(tMain, "模式",                cmbShareMain);
            AddRow(tMain, "同步方式",            cmbSyncMain);
            AddRow(tMain, "采样率 (Hz，仅独占)", numRateMain);
            AddRow(tMain, "位深 (bit，仅独占)",  numBitsMain);
            AddRow(tMain, "缓冲 (ms)",            numBufMain);

            gMain.Controls.Add(tMain);
            right.Controls.Add(gMain);

            // 1) 设备（选择并枚举）
            var gDev = new GroupBox { Text = "设备（选择并枚举）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            var tDev = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true };
            tDev.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            tDev.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));

            cmbInput.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMain.DropDownStyle  = ComboBoxStyle.DropDownList;
            cmbAux.DropDownStyle   = ComboBoxStyle.DropDownList;

            AddRow(tDev, "通道1 输入设备",  cmbInput);
            AddRow(tDev, "通道2 主输出设备", cmbMain);
            AddRow(tDev, "通道3 副输出设备", cmbAux);

            btnReload.Text = "重新枚举设备";
            btnReload.AutoSize = true;
            btnReload.Click += (s, e) => LoadDevices();
            tDev.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tDev.Controls.Add(btnReload, 1, tDev.RowCount++);
            gDev.Controls.Add(tDev);
            right.Controls.Add(gDev);

            // 底部按钮
            var pnlButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Padding = new Padding(10), AutoSize = true };
            btnOk.Text = "保存"; btnCancel.Text = "取消";
            AcceptButton = btnOk; CancelButton = btnCancel;
            btnOk.DialogResult = DialogResult.OK; btnCancel.DialogResult = DialogResult.Cancel;
            btnOk.Click += (s, e) => SaveAndClose();
            pnlButtons.Controls.AddRange(new Control[] { btnOk, btnCancel });
            Controls.Add(pnlButtons);

            // 初始化
            LoadDevices();
            LoadConfig(cur);
            RenderStatus();
        }

        // —— 数据加载 & 状态渲染 —— //
        void LoadConfig(AppSettings cur)
        {
            Result = new AppSettings
            {
                InputDeviceId=cur.InputDeviceId, MainDeviceId=cur.MainDeviceId, AuxDeviceId=cur.AuxDeviceId,
                MainShare=cur.MainShare, MainSync=cur.MainSync, MainRate=cur.MainRate, MainBits=cur.MainBits, MainBufMs=cur.MainBufMs,
                AuxShare=cur.AuxShare, AuxSync=cur.AuxSync, AuxRate=cur.AuxRate, AuxBits=cur.AuxBits, AuxBufMs=cur.AuxBufMs,
                AutoStart=cur.AutoStart, EnableLogging=cur.EnableLogging
            };
            SelectById(cmbInput, cur.InputDeviceId);
            SelectById(cmbMain,  cur.MainDeviceId);
            SelectById(cmbAux,   cur.AuxDeviceId);

            numRateMain.Value = Clamp(cur.MainRate,  (int)numRateMain.Minimum, (int)numRateMain.Maximum);
            numBitsMain.Value = Clamp(cur.MainBits,  (int)numBitsMain.Minimum, (int)numBitsMain.Maximum);
            numBufMain.Value  = Clamp(cur.MainBufMs, (int)numBufMain.Minimum,  (int)numBufMain.Maximum);
            cmbShareMain.SelectedIndex = cur.MainShare==ShareModeOption.Auto?0:(cur.MainShare==ShareModeOption.Exclusive?1:2);
            cmbSyncMain .SelectedIndex = cur.MainSync ==SyncModeOption .Auto?0:(cur.MainSync ==SyncModeOption .Event     ?1:2);

            numRateAux.Value  = Clamp(cur.AuxRate,   (int)numRateAux.Minimum,  (int)numRateAux.Maximum);
            numBitsAux.Value  = Clamp(cur.AuxBits,   (int)numBitsAux.Minimum,  (int)numBitsAux.Maximum);
            numBufAux.Value   = Clamp(cur.AuxBufMs,  (int)numBufAux.Minimum,   (int)numBufAux.Maximum);
            cmbShareAux.SelectedIndex = cur.AuxShare==ShareModeOption.Auto?0:(cur.AuxShare==ShareModeOption.Exclusive?1:2);
            cmbSyncAux .SelectedIndex = cur.AuxSync ==SyncModeOption .Auto?0:(cur.AuxSync ==SyncModeOption .Event     ?1:2);

            chkAutoStart.Checked = cur.AutoStart;
            chkLogging.Checked   = cur.EnableLogging;
        }

        void RenderStatus()
        {
            StatusSnapshot s; try { s = _statusProvider(); } catch { s = new StatusSnapshot(); }
            lblRun.Text   = s.Running ? "运行中" : "停止";
            lblInput.Text = (s.InputDevice??"-") + " | " + (s.InputRole??"-") + " | " + (s.InputFormat??"-");
            lblMain.Text  = (s.MainDevice ??"-") + " | " + (s.MainMode ??"-") + " | " + (s.MainSync ??"-");
            lblAux.Text   = (s.AuxDevice  ??"-") + " | " + (s.AuxMode  ??"-") + " | " + (s.AuxSync  ??"-");
            lblMainFmt.Text = s.MainFormat ?? "-";
            lblAuxFmt.Text  = s.AuxFormat  ?? "-";
            lblMainBuf.Text = s.MainBufferMs>0 ? (s.MainBufferMs + " ms") : "-";
            lblAuxBuf.Text  = s.AuxBufferMs >0 ? (s.AuxBufferMs  + " ms") : "-";
            lblMainPer.Text = "默认 " + s.MainDefaultPeriodMs.ToString("0.##") + " ms / 最小 " + s.MainMinimumPeriodMs.ToString("0.##") + " ms";
            lblAuxPer.Text  = "默认 " + s.AuxDefaultPeriodMs .ToString("0.##") + " ms / 最小 " + s.AuxMinimumPeriodMs .ToString("0.##") + " ms";
            lblMainPass.Text = s.MainMode=="-" ? "-" : (s.MainPassthrough ? "直通（独占&无重采样）" : (s.MainMode=="独占" ? "非直通（可能重采样）" : "不适用（共享混音）"));
            lblAuxPass.Text  = s.AuxMode=="-"  ? "-" : (s.AuxPassthrough  ? "直通（独占&无重采样）" : (s.AuxMode=="独占"  ? "非直通（可能重采样）" : "不适用（共享混音）"));
            lblMainReq.Text = s.MainBufRequestedMs>0 ? (s.MainBufRequestedMs + " ms") : "-";
            lblMainQtz.Text = s.MainBufQuantizedMs>0 ? (s.MainBufQuantizedMs + " ms") : "-";
            lblAuxReq.Text  = s.AuxBufRequestedMs>0  ? (s.AuxBufRequestedMs  + " ms") : "-";
            lblAuxQtz.Text  = s.AuxBufQuantizedMs>0  ? (s.AuxBufQuantizedMs  + " ms") : "-";
            lblMainReq.Visible = lblMainQtz.Visible = lblAuxReq.Visible = lblAuxQtz.Visible = _cfg.ShowAdvanced;
        }

        string BuildStatusText()
        {
            StatusSnapshot s; try { s = _statusProvider(); } catch { s = new StatusSnapshot(); }
            var sb = new StringBuilder(256);
            sb.AppendLine("MirrorAudio 状态");
            sb.AppendLine("运行: " + (s.Running ? "运行中" : "停止"));
            sb.AppendLine("输入: " + (s.InputDevice??"-") + " | " + (s.InputRole??"-") + " | " + (s.InputFormat??"-"));
            sb.AppendLine("主通道: " + (s.MainDevice??"-") + " | " + (s.MainMode??"-") + " | " + (s.MainSync??"-"));
            sb.AppendLine("主格式: " + (s.MainFormat??"-"));
            sb.AppendLine("主缓冲: " + (s.MainBufferMs>0 ? (s.MainBufferMs + " ms") : "-"));
            sb.AppendLine("主周期: 默认 " + s.MainDefaultPeriodMs.ToString("0.##") + " ms / 最小 " + s.MainMinimumPeriodMs.ToString("0.##") + " ms");
            sb.AppendLine("副通道: " + (s.AuxDevice??"-") + " | " + (s.AuxMode??"-") + " | " + (s.AuxSync??"-"));
            sb.AppendLine("副格式: " + (s.AuxFormat??"-"));
            sb.AppendLine("副缓冲: " + (s.AuxBufferMs>0 ? (s.AuxBufferMs + " ms") : "-"));
            sb.AppendLine("副周期: 默认 " + s.AuxDefaultPeriodMs.ToString("0.##") + " ms / 最小 " + s.AuxMinimumPeriodMs.ToString("0.##") + " ms");
            sb.AppendLine("主直通: " + (s.MainMode=="-" ? "-" : (s.MainPassthrough ? "直通" : (s.MainMode=="独占" ? "非直通" : "不适用"))));
            sb.AppendLine("副直通: " + (s.AuxMode=="-"  ? "-" : (s.AuxPassthrough  ? "直通" : (s.AuxMode=="独占"  ? "非直通" : "不适用"))));
            if(_cfg.ShowAdvanced){
                sb.AppendLine("主缓冲(请求): " + (s.MainBufRequestedMs>0 ? (s.MainBufRequestedMs + " ms") : "-"));
                sb.AppendLine("主缓冲(量化): " + (s.MainBufQuantizedMs>0 ? (s.MainBufQuantizedMs + " ms") : "-"));
                sb.AppendLine("副缓冲(请求): " + (s.AuxBufRequestedMs>0  ? (s.AuxBufRequestedMs  + " ms") : "-"));
                sb.AppendLine("副缓冲(量化): " + (s.AuxBufQuantizedMs>0  ? (s.AuxBufQuantizedMs  + " ms") : "-"));
            }
            return sb.ToString();
        }

        // —— 工具方法 —— //
        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            var l = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
            c.Dock = DockStyle.Fill;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(l, 0, t.RowCount);
            t.Controls.Add(c, 1, t.RowCount);
            t.RowCount++;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        void LoadDevices()
        {
            var mm = new MMDeviceEnumerator();

            cmbInput.Items.Clear();
            foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                cmbInput.Items.Add(new DevItem { Id = d.ID, Name = "录音: " + d.FriendlyName });
            foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                cmbInput.Items.Add(new DevItem { Id = d.ID, Name = "环回: " + d.FriendlyName });

            cmbMain.Items.Clear();
            cmbAux.Items.Clear();
            foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var it = new DevItem { Id = d.ID, Name = d.FriendlyName };
                cmbMain.Items.Add(it);
                cmbAux.Items.Add(new DevItem { Id = d.ID, Name = d.FriendlyName });
            }
        }

        void SelectById(ComboBox cmb, string id)
        {
            if (string.IsNullOrEmpty(id) || cmb.Items.Count == 0)
            {
                if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
                return;
            }
            for (int i = 0; i < cmb.Items.Count; i++)
            {
                var it = cmb.Items[i] as DevItem;
                if (it != null && it.Id == id) { cmb.SelectedIndex = i; return; }
            }
            cmb.SelectedIndex = 0;
        }

        void SaveAndClose()
        {
            var inSel   = cmbInput.SelectedItem as DevItem;
            var mainSel = cmbMain .SelectedItem as DevItem;
            var auxSel  = cmbAux  .SelectedItem as DevItem;

            if (mainSel == null || auxSel == null)
            {
                MessageBox.Show("请至少选择主/副输出设备。", "MirrorAudio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            ShareModeOption shareMain = cmbShareMain.SelectedIndex==1 ? ShareModeOption.Exclusive :
                                        (cmbShareMain.SelectedIndex==2 ? ShareModeOption.Shared : ShareModeOption.Auto);
            SyncModeOption  syncMain  = cmbSyncMain .SelectedIndex==1 ? SyncModeOption .Event :
                                        (cmbSyncMain .SelectedIndex==2 ? SyncModeOption .Polling : SyncModeOption .Auto);

            ShareModeOption shareAux  = cmbShareAux .SelectedIndex==1 ? ShareModeOption.Exclusive :
                                        (cmbShareAux .SelectedIndex==2 ? ShareModeOption.Shared : ShareModeOption.Auto);
            SyncModeOption  syncAux   = cmbSyncAux  .SelectedIndex==1 ? SyncModeOption .Event :
                                        (cmbSyncAux  .SelectedIndex==2 ? SyncModeOption .Polling : SyncModeOption .Auto);

            Result = new AppSettings
            {
                InputDeviceId = inSel != null ? inSel.Id : null,
                MainDeviceId  = mainSel.Id,
                AuxDeviceId   = auxSel.Id,
                MainShare = shareMain,
                MainSync  = syncMain,
                MainRate  = (int)numRateMain.Value,
                MainBits  = (int)numBitsMain.Value,
                MainBufMs = (int)numBufMain.Value,
                AuxShare  = shareAux,
                AuxSync   = syncAux,
                AuxRate   = (int)numRateAux.Value,
                AuxBits   = (int)numBitsAux.Value,
                AuxBufMs  = (int)numBufAux.Value,
                AutoStart = chkAutoStart.Checked,
                EnableLogging = chkLogging.Checked
            };

            // 写回全局 _cfg（由 Program.cs 提供）
            _cfg.ForcePassthrough = chkForcePass.Checked;
            _cfg.ForceRaw = chkRaw.Checked;
            _cfg.ShowAdvanced = chkAdvanced.Checked;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
