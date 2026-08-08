using System;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using OceanyaClient;
using OceanyaClient.Utilities;

namespace UnitTests
{
    /// <summary>
    /// Covers the Photoshop-style cutout selection surface used by the character creator cutting dialogs.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class CutoutSelectionSurfaceTests
    {
        private static BitmapSource CreateFrame(int width, int height)
        {
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20;
                pixels[i + 1] = 40;
                pixels[i + 2] = 60;
                pixels[i + 3] = 255;
            }

            BitmapSource frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            frame.Freeze();
            return frame;
        }

        [Test]
        public void SetSelection_ClampsOutOfBoundsSquareInsideTheFrame()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(100, 60));

            surface.SetSelection(new PixelSquareSelection(90, 50, 400), recordUndo: false);

            Assert.That(surface.Selection.HasValue, Is.True);
            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(60));
            Assert.That(surface.Selection.Value.X, Is.EqualTo(40));
            Assert.That(surface.Selection.Value.Y, Is.EqualTo(0));
        }

        [Test]
        public void UndoRedo_RestoreThePreviousSelection()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(120, 120));

            surface.SetSelection(new PixelSquareSelection(0, 0, 40), recordUndo: true);
            surface.SetSelection(new PixelSquareSelection(10, 10, 20), recordUndo: true);
            Assert.That(surface.CanUndo, Is.True);

            surface.Undo();
            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(40));
            Assert.That(surface.CanRedo, Is.True);

            surface.Redo();
            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(20));
            Assert.That(surface.Selection.Value.X, Is.EqualTo(10));
        }

        [Test]
        public void ResetHistory_DropsUndoAndRedoSteps()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(80, 80));
            surface.SetSelection(new PixelSquareSelection(0, 0, 30), recordUndo: true);

            surface.ResetHistory();

            Assert.That(surface.CanUndo, Is.False);
            Assert.That(surface.CanRedo, Is.False);
        }

        [Test]
        public void CenterSelectionToImage_KeepsSizeAndCentersTheSquare()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(200, 100));
            surface.SetSelection(new PixelSquareSelection(0, 0, 40), recordUndo: false);

            surface.CenterSelectionToImage();

            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(40));
            Assert.That(surface.Selection.Value.X, Is.EqualTo(80));
            Assert.That(surface.Selection.Value.Y, Is.EqualTo(30));
        }

        [Test]
        public void FitSelectionToImage_SelectsTheLargestCenteredSquare()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(200, 100));

            surface.FitSelectionToImage();

            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(100));
            Assert.That(surface.Selection.Value.X, Is.EqualTo(50));
            Assert.That(surface.Selection.Value.Y, Is.EqualTo(0));
        }

        [Test]
        public void SetFrame_WithSmallerFrameReclampsTheExistingSelection()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(200, 200));
            surface.SetSelection(new PixelSquareSelection(150, 150, 50), recordUndo: false);

            surface.SetFrame(CreateFrame(100, 100));

            Assert.That(surface.Selection!.Value.X + surface.Selection.Value.Size, Is.LessThanOrEqualTo(100));
            Assert.That(surface.Selection.Value.Y + surface.Selection.Value.Size, Is.LessThanOrEqualTo(100));
        }

        [Test]
        public void SetFrame_AfterClearing_RestoresContentLayoutEvenWhenZoomIsUnchanged()
        {
            // Regression: switching emotes cleared the frame (collapsing the host to 0x0) and the refit
            // landed on the same zoom, so the early-out left the image invisible and unclickable until
            // the user zoomed manually.
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(64, 64));
            double zoomAfterFirstLoad = surface.Zoom;

            surface.SetFrame(null);
            Assert.That(surface.ContentSize.Width, Is.EqualTo(0));

            surface.SetFrame(CreateFrame(64, 64));

            Assert.That(surface.Zoom, Is.EqualTo(zoomAfterFirstLoad));
            Assert.That(surface.ContentSize.Width, Is.EqualTo(64 * surface.Zoom).Within(0.001));
            Assert.That(surface.ContentSize.Height, Is.EqualTo(64 * surface.Zoom).Within(0.001));
        }

        [Test]
        public void NudgeSelection_MovesSelectionAndIsUndoable()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(100, 100));
            surface.SetSelection(new PixelSquareSelection(10, 10, 20), recordUndo: false);

            surface.NudgeSelection(1, 0);

            Assert.That(surface.Selection!.Value.X, Is.EqualTo(11));

            surface.Undo();
            Assert.That(surface.Selection!.Value.X, Is.EqualTo(10));
        }

        [Test]
        public void ResizeSelectionBy_KeepsTheSquareInsideTheFrame()
        {
            CutoutSelectionSurface surface = new CutoutSelectionSurface();
            surface.SetFrame(CreateFrame(100, 100));
            surface.SetSelection(new PixelSquareSelection(0, 0, 90), recordUndo: false);

            surface.ResizeSelectionBy(40);

            Assert.That(surface.Selection!.Value.Size, Is.EqualTo(100));
            Assert.That(surface.Selection.Value.X, Is.EqualTo(0));
        }
    }
}
