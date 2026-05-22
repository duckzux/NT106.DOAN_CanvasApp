using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    /// <summary>
    /// Toolbar icon loader (dev branch design).
    ///
    /// Background
    /// ----------
    /// The auto-generated CanvasForm.Designer.cs originally embedded
    /// toolbar icons inside CanvasForm.resx as base64-encoded image
    /// blobs. One of those blobs got corrupted, which caused two
    /// problems at the same time:
    ///
    ///   * Build:   "GenerateResource task failed -> A generic error
    ///              occurred in GDI+"  (ResGen couldn't decode the
    ///              image bytes when packing them into .resources)
    ///   * Runtime: "System.BadImageFormatException: Corrupt
    ///              .resources file ..." at the very first
    ///              resources.GetObject("btnPen.Image") call.
    ///
    /// The dev branch design avoids the embedded-image route entirely
    /// and instead loads small (16x16 / 24x24) PNG icons from
    /// Resources/Icons/ on disk. This partial class implements that:
    /// after the Designer.cs has built the form, we re-bind each
    /// toolbar button's Image from disk if a matching PNG exists.
    /// If the file is missing, the button stays in ImageAndText mode
    /// and just shows its Vietnamese text label ("Bút vẽ", "Tẩy", ...).
    ///
    /// Usage
    /// -----
    /// Call ApplyToolbarIcons() at the very end of CanvasForm's
    /// constructor (right after InitializeComponent()).  Example:
    ///
    ///     public CanvasForm()
    ///     {
    ///         InitializeComponent();
    ///         ApplyToolbarIcons();   // <-- add this line
    ///         // ... rest of constructor
    ///     }
    /// </summary>
    // Accessibility intentionally omitted so this partial merges with
    // CanvasForm.cs / CanvasForm.Designer.cs regardless of whether they
    // declare the class as `public partial class CanvasForm` or
    // `partial class CanvasForm`.
    partial class CanvasForm
    {
        /// <summary>Maps each ToolStripButton field to its PNG filename (without extension).</summary>
        private static readonly (string buttonName, string iconName)[] _iconMap = new[]
        {
            ("btnPen",       "pen"),
            ("btnEraser",    "eraser"),
            ("btnRectangle", "rectangle"),
            ("btnCircle",    "circle"),
            ("btnLine",      "line"),
            ("btnArrow",     "arrow"),
            ("btnText",      "text"),
            ("btnColor",     "color"),
            ("btnUndo",      "undo"),
            ("btnRedo",      "redo"),
            ("btnClear",     "clear"),
            ("btnExport",    "export"),
            ("btnImportBg",  "import_bg"),
            ("chkFill",      "fill"),
        };

        /// <summary>
        /// Walks the toolbar buttons and, for each one, tries to load
        /// its icon from Resources/Icons/{name}.png. Falls back to
        /// ImageAndText display (text-only) if the file is missing or
        /// unreadable.  Never throws — a missing icon is not fatal.
        /// </summary>
        private void ApplyToolbarIcons()
        {
            string iconsDir = ResolveIconsDirectory();

            foreach (var (buttonName, iconName) in _iconMap)
            {
                var button = FindToolStripButton(buttonName);
                if (button == null) continue;

                Image img = TryLoadIcon(iconsDir, iconName);

                if (img != null)
                {
                    button.Image = img;
                    button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                }
                else
                {
                    // No icon on disk -> show the Vietnamese text label
                    // we already set in the Designer, so the button is
                    // still usable.
                    button.Image = null;
                    button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                    button.AutoSize = true;
                }
            }
        }

        /// <summary>
        /// Returns the absolute path of Resources/Icons next to the
        /// running executable.  Works in both Debug/Release runs and
        /// when the binary is copied to another machine.
        /// </summary>
        private static string ResolveIconsDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDir, "Resources", "Icons");
            if (Directory.Exists(candidate)) return candidate;

            // Fallback: walk up to the project root (useful when running
            // from bin\Debug\ during development).
            try
            {
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < 4 && dir != null; i++)
                {
                    string probe = Path.Combine(dir.FullName, "Resources", "Icons");
                    if (Directory.Exists(probe)) return probe;
                    dir = dir.Parent;
                }
            }
            catch { /* ignore */ }

            return candidate; // may not exist; TryLoadIcon will just return null
        }

        /// <summary>Loads {iconsDir}/{name}.png as an Image, or null on any failure.</summary>
        private static Image TryLoadIcon(string iconsDir, string name)
        {
            try
            {
                string path = Path.Combine(iconsDir, name + ".png");
                if (!File.Exists(path)) return null;

                // Read bytes then construct from a MemoryStream so the
                // file isn't locked for the lifetime of the Image
                // (Image.FromFile keeps the handle open).
                byte[] bytes = File.ReadAllBytes(path);
                return Image.FromStream(new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Looks up a ToolStripButton field by name via reflection on the partial class.</summary>
        private ToolStripButton FindToolStripButton(string fieldName)
        {
            var f = typeof(CanvasForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f?.GetValue(this) as ToolStripButton;
        }
    }
}
