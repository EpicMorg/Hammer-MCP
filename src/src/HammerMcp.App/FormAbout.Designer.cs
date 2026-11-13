namespace HammerMcp.App;

partial class FormAbout
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
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormAbout));
        labelName = new Label();
        pictureBox = new PictureBox();
        labelVersion = new Label();
        buttonOK = new Button();
        labelnfo = new Label();
        labelLicense = new Label();
        linkLabelSources = new LinkLabel();
        linkLabelSiteAuthor = new LinkLabel();
        ((System.ComponentModel.ISupportInitialize)pictureBox).BeginInit();
        SuspendLayout();
        // 
        // labelName
        // 
        labelName.AutoSize = true;
        labelName.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 204);
        labelName.Location = new Point(66, 12);
        labelName.Name = "labelName";
        labelName.Size = new Size(136, 17);
        labelName.TabIndex = 3;
        labelName.Text = "Hammer MCP Server";
        // 
        // pictureBox
        // 
        pictureBox.BorderStyle = BorderStyle.FixedSingle;
        pictureBox.Image = Properties.Resources.hammer_mcp_red_48;
        pictureBox.Location = new Point(12, 12);
        pictureBox.Name = "pictureBox";
        pictureBox.Size = new Size(48, 48);
        pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
        pictureBox.TabIndex = 2;
        pictureBox.TabStop = false;
        // 
        // labelVersion
        // 
        labelVersion.AutoSize = true;
        labelVersion.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 204);
        labelVersion.Location = new Point(66, 29);
        labelVersion.Name = "labelVersion";
        labelVersion.Size = new Size(60, 13);
        labelVersion.TabIndex = 3;
        labelVersion.Text = "<version>";
        // 
        // buttonOK
        // 
        buttonOK.Location = new Point(394, 173);
        buttonOK.Name = "buttonOK";
        buttonOK.Size = new Size(75, 23);
        buttonOK.TabIndex = 4;
        buttonOK.Text = "OK";
        buttonOK.UseVisualStyleBackColor = true;
        // 
        // labelnfo
        // 
        labelnfo.BorderStyle = BorderStyle.FixedSingle;
        labelnfo.Location = new Point(12, 63);
        labelnfo.Name = "labelnfo";
        labelnfo.Size = new Size(457, 107);
        labelnfo.TabIndex = 5;
        labelnfo.Text = resources.GetString("labelnfo.Text");
        // 
        // labelLicense
        // 
        labelLicense.AutoSize = true;
        labelLicense.Location = new Point(76, 177);
        labelLicense.Name = "labelLicense";
        labelLicense.Size = new Size(68, 15);
        labelLicense.TabIndex = 6;
        labelLicense.Text = "(c) <YYYY>";
        // 
        // linkLabelSources
        // 
        linkLabelSources.AccessibleDescription = "https://github.com/epicmorg/hammer-mcp";
        linkLabelSources.AutoSize = true;
        linkLabelSources.Location = new Point(66, 45);
        linkLabelSources.Name = "linkLabelSources";
        linkLabelSources.Size = new Size(48, 15);
        linkLabelSources.TabIndex = 7;
        linkLabelSources.TabStop = true;
        linkLabelSources.Text = "Sources";
        // 
        // linkLabelSiteAuthor
        // 
        linkLabelSiteAuthor.AccessibleDescription = "https://epicm.org/";
        linkLabelSiteAuthor.AutoSize = true;
        linkLabelSiteAuthor.Location = new Point(12, 177);
        linkLabelSiteAuthor.Name = "linkLabelSiteAuthor";
        linkLabelSiteAuthor.Size = new Size(58, 15);
        linkLabelSiteAuthor.TabIndex = 7;
        linkLabelSiteAuthor.TabStop = true;
        linkLabelSiteAuthor.Text = "EpicMorg";
        // 
        // FormAbout
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(481, 208);
        Controls.Add(linkLabelSiteAuthor);
        Controls.Add(linkLabelSources);
        Controls.Add(labelLicense);
        Controls.Add(labelnfo);
        Controls.Add(buttonOK);
        Controls.Add(labelVersion);
        Controls.Add(labelName);
        Controls.Add(pictureBox);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        Icon = (Icon)resources.GetObject("$this.Icon");
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "FormAbout";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "About";
        ((System.ComponentModel.ISupportInitialize)pictureBox).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Label labelName;
    private PictureBox pictureBox;
    private Label labelVersion;
    private Button buttonOK;
    private Label labelnfo;
    private Label labelLicense;
    private LinkLabel linkLabelSources;
    private LinkLabel linkLabelSiteAuthor;
}