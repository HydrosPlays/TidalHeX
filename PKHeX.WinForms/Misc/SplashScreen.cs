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
    /// Larger branded splash: logo, wordmark and status on the ocean backdrop.
    /// Runs on the splash thread, so everything here is created locally (no shared GDI objects).
    /// </summary>
    private void ApplyTidalLayout()
    {
        const int width = 360;
        const int height = 250;
        const int logo = 124;
        SuspendLayout();
        AutoSize = false;
        ClientSize = new System.Drawing.Size(width, height);
        BackgroundImage = Tidal.TidalPaint.RenderSurface(ClientSize, Tidal.TidalPaint.Surface.Backdrop);
        BackgroundImageLayout = ImageLayout.None;

        PB_Icon.BackgroundImage = Tidal.TidalAssets.GetLogo(logo);
        PB_Icon.BackgroundImageLayout = ImageLayout.Center;
        PB_Icon.BackColor = System.Drawing.Color.Transparent;
        PB_Icon.SetBounds((width - logo) / 2, 26, logo, logo);

        L_Site.AutoSize = false;
        L_Site.Text = "TidalHeX";
        L_Site.Font = new System.Drawing.Font("Segoe UI Semibold", 17f);
        L_Site.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        L_Site.SetBounds(0, 156, width, 36);

        L_Status.AutoSize = false;
        L_Status.Text = "Starting up...";
        L_Status.Font = new System.Drawing.Font("Segoe UI", 9.5f);
        L_Status.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        L_Status.SetBounds(0, 196, width, 24);

        foreach (var label in new[] { L_Status, L_Site })
        {
            label.BackColor = System.Drawing.Color.Transparent;
            label.ForeColor = System.Drawing.Color.White;
        }
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
