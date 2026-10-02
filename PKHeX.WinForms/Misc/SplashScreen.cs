using System.Windows.Forms;

namespace PKHeX.WinForms;

public partial class SplashScreen : Form
{
    public SplashScreen()
    {
        InitializeComponent();
        if (Tidal.TidalTheme.Enabled)
            ApplyTidalLayout();
    }

    /// <summary>
    /// Larger branded splash: the logo with its wordmark, and the status, on the ocean backdrop.
    /// Runs on the splash thread, so everything here is created locally (no shared GDI objects).
    /// </summary>
    private void ApplyTidalLayout()
    {
        const int width = 360;
        const int height = 268;
        const int logoWidth = 280; // the logo is 4:3
        const int logoHeight = logoWidth * 3 / 4;
        SuspendLayout();
        AutoSize = false;
        ClientSize = new System.Drawing.Size(width, height);
        BackgroundImage = Tidal.TidalPaint.RenderSurface(ClientSize, Tidal.TidalPaint.Surface.Backdrop);
        BackgroundImageLayout = ImageLayout.None;

        PB_Icon.BackgroundImage = Tidal.TidalAssets.GetLogoWithName(logoWidth);
        PB_Icon.BackgroundImageLayout = ImageLayout.Center;
        PB_Icon.BackColor = System.Drawing.Color.Transparent;
        PB_Icon.SetBounds((width - logoWidth) / 2, 18, logoWidth, logoHeight);

        L_Site.Visible = false; // the name is part of the logo

        L_Status.AutoSize = false;
        L_Status.Text = "Starting up...";
        L_Status.Font = new System.Drawing.Font("Segoe UI", 9.5f);
        L_Status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        L_Status.SetBounds(0, 18 + logoHeight + 14, width, 24);
        L_Status.BackColor = System.Drawing.Color.Transparent;
        L_Status.ForeColor = Tidal.TidalPalette.TextSoft;
        ResumeLayout(true);
        HandleCreated += (_, _) => Tidal.TidalTheme.RoundCorners(this);
    }

    private bool CanClose;

    private void SplashScreen_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!CanClose)
            e.Cancel = true;
    }

    public void ForceClose()
    {
        CanClose = true;
        Close();
    }
}
