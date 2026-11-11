namespace HammerMcp.App;

partial class FormMain
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
        statusStrip1 = new StatusStrip();
        toolStripStatusAttaching = new ToolStripStatusLabel();
        menuStrip1 = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        exitToolStripMenuItem = new ToolStripMenuItem();
        helpToolStripMenuItem = new ToolStripMenuItem();
        aboutToolStripMenuItem = new ToolStripMenuItem();
        tabControl = new TabControl();
        tabPageMain = new TabPage();
        buttonServerStartStop = new Button();
        groupBoxProcess = new GroupBox();
        groupBoxMetaInfoFromHammer = new GroupBox();
        dataGridViewMetaInfoFromHammer = new DataGridView();
        ColumnKey = new DataGridViewTextBoxColumn();
        ColumnValue = new DataGridViewTextBoxColumn();
        checkBoxShowProcess = new CheckBox();
        buttonAttachOrDetach = new Button();
        comboBoxProcessList = new ComboBox();
        labelProcessName = new Label();
        pictureBoxProcessImage = new PictureBox();
        tabPageSettings = new TabPage();
        button1 = new Button();
        buttonApply = new Button();
        groupBoxSettingsMain = new GroupBox();
        textBoxMCPModeHelpText = new TextBox();
        groupBoxNetworkSettings = new GroupBox();
        labelPort = new Label();
        textBoxPort = new TextBox();
        labelJustLabel = new Label();
        buttonGenerateSecurityToken = new Button();
        buttonCopySecurityToken = new Button();
        textBoxSecurityToken = new TextBox();
        labelSecurityToken = new Label();
        labelAdaptersAndAdresses = new Label();
        comboBoxAdaptersAndAdresses = new ComboBox();
        labelMCPMode = new Label();
        comboBoxMCPMode = new ComboBox();
        tabPage1 = new TabPage();
        textBoxLiveLogs = new TextBox();
        statusStrip1.SuspendLayout();
        menuStrip1.SuspendLayout();
        tabControl.SuspendLayout();
        tabPageMain.SuspendLayout();
        groupBoxProcess.SuspendLayout();
        groupBoxMetaInfoFromHammer.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dataGridViewMetaInfoFromHammer).BeginInit();
        ((System.ComponentModel.ISupportInitialize)pictureBoxProcessImage).BeginInit();
        tabPageSettings.SuspendLayout();
        groupBoxSettingsMain.SuspendLayout();
        groupBoxNetworkSettings.SuspendLayout();
        tabPage1.SuspendLayout();
        SuspendLayout();
        // 
        // statusStrip1
        // 
        statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusAttaching });
        statusStrip1.Location = new Point(0, 389);
        statusStrip1.Name = "statusStrip1";
        statusStrip1.Size = new Size(637, 22);
        statusStrip1.TabIndex = 0;
        statusStrip1.Text = "statusStrip1";
        // 
        // toolStripStatusAttaching
        // 
        toolStripStatusAttaching.Image = Properties.Resources.warning_circle_16;
        toolStripStatusAttaching.Name = "toolStripStatusAttaching";
        toolStripStatusAttaching.Size = new Size(130, 17);
        toolStripStatusAttaching.Text = "Process not selected";
        // 
        // menuStrip1
        // 
        menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, helpToolStripMenuItem });
        menuStrip1.Location = new Point(0, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new Size(637, 24);
        menuStrip1.TabIndex = 2;
        menuStrip1.Text = "menuStrip1";
        // 
        // fileToolStripMenuItem
        // 
        fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { exitToolStripMenuItem });
        fileToolStripMenuItem.Name = "fileToolStripMenuItem";
        fileToolStripMenuItem.Size = new Size(37, 20);
        fileToolStripMenuItem.Text = "File";
        // 
        // exitToolStripMenuItem
        // 
        exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        exitToolStripMenuItem.Size = new Size(92, 22);
        exitToolStripMenuItem.Text = "Exit";
        // 
        // helpToolStripMenuItem
        // 
        helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { aboutToolStripMenuItem });
        helpToolStripMenuItem.Name = "helpToolStripMenuItem";
        helpToolStripMenuItem.Size = new Size(44, 20);
        helpToolStripMenuItem.Text = "Help";
        // 
        // aboutToolStripMenuItem
        // 
        aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
        aboutToolStripMenuItem.Size = new Size(107, 22);
        aboutToolStripMenuItem.Text = "About";
        // 
        // tabControl
        // 
        tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tabControl.Controls.Add(tabPageMain);
        tabControl.Controls.Add(tabPageSettings);
        tabControl.Controls.Add(tabPage1);
        tabControl.Location = new Point(12, 27);
        tabControl.Name = "tabControl";
        tabControl.SelectedIndex = 0;
        tabControl.Size = new Size(613, 359);
        tabControl.TabIndex = 3;
        // 
        // tabPageMain
        // 
        tabPageMain.Controls.Add(buttonServerStartStop);
        tabPageMain.Controls.Add(groupBoxProcess);
        tabPageMain.Location = new Point(4, 24);
        tabPageMain.Name = "tabPageMain";
        tabPageMain.Padding = new Padding(3);
        tabPageMain.Size = new Size(605, 331);
        tabPageMain.TabIndex = 0;
        tabPageMain.Text = "Main";
        tabPageMain.UseVisualStyleBackColor = true;
        // 
        // buttonServerStartStop
        // 
        buttonServerStartStop.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        buttonServerStartStop.Location = new Point(524, 302);
        buttonServerStartStop.Name = "buttonServerStartStop";
        buttonServerStartStop.Size = new Size(75, 23);
        buttonServerStartStop.TabIndex = 1;
        buttonServerStartStop.Text = "Start";
        buttonServerStartStop.UseVisualStyleBackColor = true;
        // 
        // groupBoxProcess
        // 
        groupBoxProcess.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        groupBoxProcess.Controls.Add(groupBoxMetaInfoFromHammer);
        groupBoxProcess.Controls.Add(checkBoxShowProcess);
        groupBoxProcess.Controls.Add(buttonAttachOrDetach);
        groupBoxProcess.Controls.Add(comboBoxProcessList);
        groupBoxProcess.Controls.Add(labelProcessName);
        groupBoxProcess.Controls.Add(pictureBoxProcessImage);
        groupBoxProcess.Location = new Point(6, 6);
        groupBoxProcess.Name = "groupBoxProcess";
        groupBoxProcess.Size = new Size(593, 290);
        groupBoxProcess.TabIndex = 0;
        groupBoxProcess.TabStop = false;
        groupBoxProcess.Text = "Process:";
        // 
        // groupBoxMetaInfoFromHammer
        // 
        groupBoxMetaInfoFromHammer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        groupBoxMetaInfoFromHammer.Controls.Add(dataGridViewMetaInfoFromHammer);
        groupBoxMetaInfoFromHammer.Enabled = false;
        groupBoxMetaInfoFromHammer.Location = new Point(6, 104);
        groupBoxMetaInfoFromHammer.Name = "groupBoxMetaInfoFromHammer";
        groupBoxMetaInfoFromHammer.Size = new Size(581, 180);
        groupBoxMetaInfoFromHammer.TabIndex = 5;
        groupBoxMetaInfoFromHammer.TabStop = false;
        groupBoxMetaInfoFromHammer.Text = "Meta info:";
        // 
        // dataGridViewMetaInfoFromHammer
        // 
        dataGridViewMetaInfoFromHammer.AllowUserToAddRows = false;
        dataGridViewMetaInfoFromHammer.AllowUserToDeleteRows = false;
        dataGridViewMetaInfoFromHammer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dataGridViewMetaInfoFromHammer.BackgroundColor = SystemColors.Control;
        dataGridViewMetaInfoFromHammer.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dataGridViewMetaInfoFromHammer.Columns.AddRange(new DataGridViewColumn[] { ColumnKey, ColumnValue });
        dataGridViewMetaInfoFromHammer.Location = new Point(6, 22);
        dataGridViewMetaInfoFromHammer.Name = "dataGridViewMetaInfoFromHammer";
        dataGridViewMetaInfoFromHammer.ReadOnly = true;
        dataGridViewMetaInfoFromHammer.Size = new Size(569, 152);
        dataGridViewMetaInfoFromHammer.TabIndex = 0;
        // 
        // ColumnKey
        // 
        ColumnKey.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        ColumnKey.HeaderText = "Key";
        ColumnKey.Name = "ColumnKey";
        ColumnKey.ReadOnly = true;
        ColumnKey.Width = 51;
        // 
        // ColumnValue
        // 
        ColumnValue.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        ColumnValue.HeaderText = "Value";
        ColumnValue.Name = "ColumnValue";
        ColumnValue.ReadOnly = true;
        // 
        // checkBoxShowProcess
        // 
        checkBoxShowProcess.AutoSize = true;
        checkBoxShowProcess.Location = new Point(6, 80);
        checkBoxShowProcess.Name = "checkBoxShowProcess";
        checkBoxShowProcess.Size = new Size(124, 19);
        checkBoxShowProcess.TabIndex = 4;
        checkBoxShowProcess.Text = "Show all processes";
        checkBoxShowProcess.UseVisualStyleBackColor = true;
        // 
        // buttonAttachOrDetach
        // 
        buttonAttachOrDetach.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        buttonAttachOrDetach.Location = new Point(512, 76);
        buttonAttachOrDetach.Name = "buttonAttachOrDetach";
        buttonAttachOrDetach.Size = new Size(75, 23);
        buttonAttachOrDetach.TabIndex = 3;
        buttonAttachOrDetach.Text = "Attach";
        buttonAttachOrDetach.UseVisualStyleBackColor = true;
        // 
        // comboBoxProcessList
        // 
        comboBoxProcessList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        comboBoxProcessList.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxProcessList.FormattingEnabled = true;
        comboBoxProcessList.Location = new Point(60, 47);
        comboBoxProcessList.Name = "comboBoxProcessList";
        comboBoxProcessList.Size = new Size(527, 23);
        comboBoxProcessList.TabIndex = 2;
        // 
        // labelProcessName
        // 
        labelProcessName.AutoSize = true;
        labelProcessName.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 204);
        labelProcessName.Location = new Point(60, 22);
        labelProcessName.Name = "labelProcessName";
        labelProcessName.Size = new Size(167, 17);
        labelProcessName.TabIndex = 1;
        labelProcessName.Text = "<Selected Process Name>";
        // 
        // pictureBoxProcessImage
        // 
        pictureBoxProcessImage.BorderStyle = BorderStyle.FixedSingle;
        pictureBoxProcessImage.Image = Properties.Resources.hammer_mcp_red_48;
        pictureBoxProcessImage.Location = new Point(6, 22);
        pictureBoxProcessImage.Name = "pictureBoxProcessImage";
        pictureBoxProcessImage.Size = new Size(48, 48);
        pictureBoxProcessImage.SizeMode = PictureBoxSizeMode.CenterImage;
        pictureBoxProcessImage.TabIndex = 0;
        pictureBoxProcessImage.TabStop = false;
        // 
        // tabPageSettings
        // 
        tabPageSettings.Controls.Add(button1);
        tabPageSettings.Controls.Add(buttonApply);
        tabPageSettings.Controls.Add(groupBoxSettingsMain);
        tabPageSettings.Location = new Point(4, 24);
        tabPageSettings.Name = "tabPageSettings";
        tabPageSettings.Padding = new Padding(3);
        tabPageSettings.Size = new Size(605, 331);
        tabPageSettings.TabIndex = 1;
        tabPageSettings.Text = "Settings";
        tabPageSettings.UseVisualStyleBackColor = true;
        // 
        // button1
        // 
        button1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        button1.Location = new Point(6, 302);
        button1.Name = "button1";
        button1.Size = new Size(75, 23);
        button1.TabIndex = 4;
        button1.Text = "Reset";
        button1.UseVisualStyleBackColor = true;
        // 
        // buttonApply
        // 
        buttonApply.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        buttonApply.Location = new Point(524, 302);
        buttonApply.Name = "buttonApply";
        buttonApply.Size = new Size(75, 23);
        buttonApply.TabIndex = 3;
        buttonApply.Text = "Apply";
        buttonApply.UseVisualStyleBackColor = true;
        // 
        // groupBoxSettingsMain
        // 
        groupBoxSettingsMain.Controls.Add(textBoxMCPModeHelpText);
        groupBoxSettingsMain.Controls.Add(groupBoxNetworkSettings);
        groupBoxSettingsMain.Controls.Add(labelMCPMode);
        groupBoxSettingsMain.Controls.Add(comboBoxMCPMode);
        groupBoxSettingsMain.Location = new Point(6, 6);
        groupBoxSettingsMain.Name = "groupBoxSettingsMain";
        groupBoxSettingsMain.Size = new Size(593, 290);
        groupBoxSettingsMain.TabIndex = 2;
        groupBoxSettingsMain.TabStop = false;
        groupBoxSettingsMain.Text = "Main:";
        // 
        // textBoxMCPModeHelpText
        // 
        textBoxMCPModeHelpText.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        textBoxMCPModeHelpText.BorderStyle = BorderStyle.FixedSingle;
        textBoxMCPModeHelpText.Location = new Point(174, 19);
        textBoxMCPModeHelpText.Multiline = true;
        textBoxMCPModeHelpText.Name = "textBoxMCPModeHelpText";
        textBoxMCPModeHelpText.PlaceholderText = "Help text about selected mode";
        textBoxMCPModeHelpText.ReadOnly = true;
        textBoxMCPModeHelpText.ScrollBars = ScrollBars.Both;
        textBoxMCPModeHelpText.Size = new Size(407, 86);
        textBoxMCPModeHelpText.TabIndex = 3;
        // 
        // groupBoxNetworkSettings
        // 
        groupBoxNetworkSettings.Controls.Add(labelPort);
        groupBoxNetworkSettings.Controls.Add(textBoxPort);
        groupBoxNetworkSettings.Controls.Add(labelJustLabel);
        groupBoxNetworkSettings.Controls.Add(buttonGenerateSecurityToken);
        groupBoxNetworkSettings.Controls.Add(buttonCopySecurityToken);
        groupBoxNetworkSettings.Controls.Add(textBoxSecurityToken);
        groupBoxNetworkSettings.Controls.Add(labelSecurityToken);
        groupBoxNetworkSettings.Controls.Add(labelAdaptersAndAdresses);
        groupBoxNetworkSettings.Controls.Add(comboBoxAdaptersAndAdresses);
        groupBoxNetworkSettings.Location = new Point(6, 111);
        groupBoxNetworkSettings.Name = "groupBoxNetworkSettings";
        groupBoxNetworkSettings.Size = new Size(581, 173);
        groupBoxNetworkSettings.TabIndex = 2;
        groupBoxNetworkSettings.TabStop = false;
        groupBoxNetworkSettings.Text = "Network settings:";
        // 
        // labelPort
        // 
        labelPort.AutoSize = true;
        labelPort.Location = new Point(500, 19);
        labelPort.Name = "labelPort";
        labelPort.Size = new Size(32, 15);
        labelPort.TabIndex = 10;
        labelPort.Text = "Port:";
        // 
        // textBoxPort
        // 
        textBoxPort.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        textBoxPort.Location = new Point(500, 37);
        textBoxPort.Name = "textBoxPort";
        textBoxPort.PlaceholderText = "9820";
        textBoxPort.Size = new Size(75, 23);
        textBoxPort.TabIndex = 9;
        // 
        // labelJustLabel
        // 
        labelJustLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        labelJustLabel.AutoSize = true;
        labelJustLabel.Location = new Point(484, 40);
        labelJustLabel.Name = "labelJustLabel";
        labelJustLabel.Size = new Size(10, 15);
        labelJustLabel.TabIndex = 8;
        labelJustLabel.Text = ":";
        // 
        // buttonGenerateSecurityToken
        // 
        buttonGenerateSecurityToken.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        buttonGenerateSecurityToken.Location = new Point(419, 110);
        buttonGenerateSecurityToken.Name = "buttonGenerateSecurityToken";
        buttonGenerateSecurityToken.Size = new Size(156, 23);
        buttonGenerateSecurityToken.TabIndex = 7;
        buttonGenerateSecurityToken.Text = "Generate";
        buttonGenerateSecurityToken.UseVisualStyleBackColor = true;
        // 
        // buttonCopySecurityToken
        // 
        buttonCopySecurityToken.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        buttonCopySecurityToken.Location = new Point(500, 81);
        buttonCopySecurityToken.Name = "buttonCopySecurityToken";
        buttonCopySecurityToken.Size = new Size(75, 23);
        buttonCopySecurityToken.TabIndex = 4;
        buttonCopySecurityToken.Text = "Copy";
        buttonCopySecurityToken.UseVisualStyleBackColor = true;
        // 
        // textBoxSecurityToken
        // 
        textBoxSecurityToken.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        textBoxSecurityToken.Location = new Point(6, 81);
        textBoxSecurityToken.Name = "textBoxSecurityToken";
        textBoxSecurityToken.Size = new Size(488, 23);
        textBoxSecurityToken.TabIndex = 3;
        // 
        // labelSecurityToken
        // 
        labelSecurityToken.AutoSize = true;
        labelSecurityToken.Location = new Point(6, 63);
        labelSecurityToken.Name = "labelSecurityToken";
        labelSecurityToken.Size = new Size(85, 15);
        labelSecurityToken.TabIndex = 2;
        labelSecurityToken.Text = "Security token:";
        // 
        // labelAdaptersAndAdresses
        // 
        labelAdaptersAndAdresses.AutoSize = true;
        labelAdaptersAndAdresses.Location = new Point(6, 19);
        labelAdaptersAndAdresses.Name = "labelAdaptersAndAdresses";
        labelAdaptersAndAdresses.Size = new Size(105, 15);
        labelAdaptersAndAdresses.TabIndex = 1;
        labelAdaptersAndAdresses.Text = "Available adresses:";
        // 
        // comboBoxAdaptersAndAdresses
        // 
        comboBoxAdaptersAndAdresses.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        comboBoxAdaptersAndAdresses.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxAdaptersAndAdresses.FormattingEnabled = true;
        comboBoxAdaptersAndAdresses.Items.AddRange(new object[] { "localhost", "All", "full list of available adresses" });
        comboBoxAdaptersAndAdresses.Location = new Point(6, 37);
        comboBoxAdaptersAndAdresses.Name = "comboBoxAdaptersAndAdresses";
        comboBoxAdaptersAndAdresses.Size = new Size(472, 23);
        comboBoxAdaptersAndAdresses.TabIndex = 0;
        // 
        // labelMCPMode
        // 
        labelMCPMode.AutoSize = true;
        labelMCPMode.Location = new Point(6, 19);
        labelMCPMode.Name = "labelMCPMode";
        labelMCPMode.Size = new Size(70, 15);
        labelMCPMode.TabIndex = 1;
        labelMCPMode.Text = "MCP Mode:";
        // 
        // comboBoxMCPMode
        // 
        comboBoxMCPMode.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoxMCPMode.FormattingEnabled = true;
        comboBoxMCPMode.Items.AddRange(new object[] { "Auto", "Basic", "Shell", "Advanced" });
        comboBoxMCPMode.Location = new Point(6, 37);
        comboBoxMCPMode.Name = "comboBoxMCPMode";
        comboBoxMCPMode.Size = new Size(162, 23);
        comboBoxMCPMode.TabIndex = 0;
        // 
        // tabPage1
        // 
        tabPage1.Controls.Add(textBoxLiveLogs);
        tabPage1.Location = new Point(4, 24);
        tabPage1.Name = "tabPage1";
        tabPage1.Padding = new Padding(3);
        tabPage1.Size = new Size(605, 331);
        tabPage1.TabIndex = 2;
        tabPage1.Text = "Logs";
        tabPage1.UseVisualStyleBackColor = true;
        // 
        // textBoxLiveLogs
        // 
        textBoxLiveLogs.Dock = DockStyle.Fill;
        textBoxLiveLogs.Location = new Point(3, 3);
        textBoxLiveLogs.Multiline = true;
        textBoxLiveLogs.Name = "textBoxLiveLogs";
        textBoxLiveLogs.PlaceholderText = "Live logs will be here...";
        textBoxLiveLogs.ReadOnly = true;
        textBoxLiveLogs.ScrollBars = ScrollBars.Both;
        textBoxLiveLogs.Size = new Size(599, 325);
        textBoxLiveLogs.TabIndex = 4;
        // 
        // FormMain
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(637, 411);
        Controls.Add(tabControl);
        Controls.Add(statusStrip1);
        Controls.Add(menuStrip1);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        Icon = (Icon)resources.GetObject("$this.Icon");
        MainMenuStrip = menuStrip1;
        MaximizeBox = false;
        Name = "FormMain";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Hammer MCP Server";
        statusStrip1.ResumeLayout(false);
        statusStrip1.PerformLayout();
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        tabControl.ResumeLayout(false);
        tabPageMain.ResumeLayout(false);
        groupBoxProcess.ResumeLayout(false);
        groupBoxProcess.PerformLayout();
        groupBoxMetaInfoFromHammer.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dataGridViewMetaInfoFromHammer).EndInit();
        ((System.ComponentModel.ISupportInitialize)pictureBoxProcessImage).EndInit();
        tabPageSettings.ResumeLayout(false);
        groupBoxSettingsMain.ResumeLayout(false);
        groupBoxSettingsMain.PerformLayout();
        groupBoxNetworkSettings.ResumeLayout(false);
        groupBoxNetworkSettings.PerformLayout();
        tabPage1.ResumeLayout(false);
        tabPage1.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private StatusStrip statusStrip1;
    private ToolStripStatusLabel toolStripStatusAttaching;
    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem exitToolStripMenuItem;
    private ToolStripMenuItem helpToolStripMenuItem;
    private ToolStripMenuItem aboutToolStripMenuItem;
    private TabControl tabControl;
    private TabPage tabPageMain;
    private TabPage tabPageSettings;
    private GroupBox groupBoxProcess;
    private Button buttonServerStartStop;
    private PictureBox pictureBoxProcessImage;
    private ComboBox comboBoxProcessList;
    private Label labelProcessName;
    private Button buttonAttachOrDetach;
    private CheckBox checkBoxShowProcess;
    private GroupBox groupBoxMetaInfoFromHammer;
    private DataGridView dataGridViewMetaInfoFromHammer;
    private DataGridViewTextBoxColumn ColumnKey;
    private DataGridViewTextBoxColumn ColumnValue;
    private ComboBox comboBoxMCPMode;

    private Button buttonApply;
    private GroupBox groupBoxNetworkSettings;
    private Label labelMCPMode;
    private GroupBox groupBoxSettingsMain;
    private Label labelAdaptersAndAdresses;
    private ComboBox comboBoxAdaptersAndAdresses;
    private Button button1;
    private TextBox textBoxSecurityToken;
    private Button buttonCopySecurityToken;
    private Label labelSecurityToken;
    private Button buttonGenerateSecurityToken;
    private TextBox textBoxMCPModeHelpText;
    private TabPage tabPage1;
    private TextBox textBoxLiveLogs;
    private Label labelJustLabel;
    private TextBox textBoxPort;
    private Label labelPort;
}
