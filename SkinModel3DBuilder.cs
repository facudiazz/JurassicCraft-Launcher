using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace JurassicCraftLauncher
{
    public static class SkinModel3DBuilder
    {
        private const int SCALE = 16; // nearest-neighbor upscale para evitar blur WPF 3D

        public static Model3DGroup BuildModel(BitmapSource skinTexture, bool isSlim)
        {
            skinTexture = NormalizeSkinTexture(skinTexture);

            // Escalar el atlas completo con nearest-neighbor puro
            var scaledAtlas = ScaleNearestNeighbor(skinTexture, SCALE);

            var group = new Model3DGroup();
            int armW = isSlim ? 3 : 4;
            double armX = isSlim ? 5.5 : 6.0;

            // ── CAPA BASE ─────────────────────────────────────────────
            AddBox(group, scaledAtlas, 8, 8, 8, new Point3D(0, 28, 0), 0, 0);
            AddBox(group, scaledAtlas, 8, 12, 4, new Point3D(0, 18, 0), 16, 16);
            AddBox(group, scaledAtlas, 4, 12, 4, new Point3D(2, 6, 0), 0, 16);
            AddBox(group, scaledAtlas, 4, 12, 4, new Point3D(-2, 6, 0), 16, 48);
            AddBox(group, scaledAtlas, armW, 12, 4, new Point3D(armX, 18, 0), 40, 16);
            AddBox(group, scaledAtlas, armW, 12, 4, new Point3D(-armX, 18, 0), 32, 48);

            // ── CAPA OVERLAY ──────────────────────────────────────────
            double eH = 1.0 / 8.0;
            double eB = 0.5 / 8.0;
            double eL = 0.5 / 4.0;
            double eA = 0.5 / armW;

            AddBox(group, scaledAtlas, 8, 8, 8, new Point3D(0, 28, 0), 32, 0, 1 + eH);
            AddBox(group, scaledAtlas, 8, 12, 4, new Point3D(0, 18, 0), 16, 32, 1 + eB);
            AddBox(group, scaledAtlas, 4, 12, 4, new Point3D(2, 6, 0), 0, 32, 1 + eL);
            AddBox(group, scaledAtlas, 4, 12, 4, new Point3D(-2, 6, 0), 0, 48, 1 + eL);
            AddBox(group, scaledAtlas, armW, 12, 4, new Point3D(armX, 18, 0), 40, 32, 1 + eA);
            AddBox(group, scaledAtlas, armW, 12, 4, new Point3D(-armX, 18, 0), 48, 48, 1 + eA);

            return group;
        }

        private static BitmapSource NormalizeSkinTexture(BitmapSource source)
        {
            var fmt = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            if (fmt.PixelWidth != 64 || fmt.PixelHeight != 32)
                return fmt;

            int srcStride = fmt.PixelWidth * 4;
            var srcPixels = new byte[srcStride * fmt.PixelHeight];
            fmt.CopyPixels(srcPixels, srcStride, 0);

            const int dstWidth = 64;
            const int dstHeight = 64;
            int dstStride = dstWidth * 4;
            var dstPixels = new byte[dstStride * dstHeight];

            Array.Copy(srcPixels, dstPixels, srcPixels.Length);

            // Copiar la pierna derecha clásica a la pierna izquierda moderna.
            CopyRectMirrored(srcPixels, srcStride, dstPixels, dstStride, 0, 16, 16, 16, 16, 48);

            // Copiar el brazo derecho clásico al brazo izquierdo moderno.
            CopyRectMirrored(srcPixels, srcStride, dstPixels, dstStride, 40, 16, 16, 16, 32, 48);

            return BitmapSource.Create(
                dstWidth,
                dstHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                dstPixels,
                dstStride);
        }

        private static void CopyRectMirrored(
            byte[] srcPixels,
            int srcStride,
            byte[] dstPixels,
            int dstStride,
            int srcX,
            int srcY,
            int width,
            int height,
            int dstX,
            int dstY)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mirroredX = width - 1 - x;
                    int srcIndex = ((srcY + y) * srcStride) + ((srcX + mirroredX) * 4);
                    int dstIndex = ((dstY + y) * dstStride) + ((dstX + x) * 4);

                    dstPixels[dstIndex] = srcPixels[srcIndex];
                    dstPixels[dstIndex + 1] = srcPixels[srcIndex + 1];
                    dstPixels[dstIndex + 2] = srcPixels[srcIndex + 2];
                    dstPixels[dstIndex + 3] = srcPixels[srcIndex + 3];
                }
            }
        }

        private static void AddBox(
            Model3DGroup group,
            BitmapSource scaledAtlas,
            int w, int h, int d,
            Point3D center,
            int ox, int oy,
            double scale = 1.0)
        {
            double sw = w * scale, sh = h * scale, sd = d * scale;
            double hw = sw / 2, hh = sh / 2, hd = sd / 2;

            // (atlasX, atlasY, atlasW, atlasH, flipU) — coordenadas en el atlas ORIGINAL (64x64)
            var faceAtlas = new (int ax, int ay, int aw, int ah, bool flipU)[]
            {
                (ox,           oy + d, d,  h,  true),  // Right (+X)
                (ox + d + w,   oy + d, d,  h,  true),  // Left  (-X)
                (ox + d,       oy,     w,  d,  false), // Top   (+Y)
                (ox + d + w,   oy,     w,  d,  true),  // Bottom(-Y)
                (ox + d,       oy + d, w,  h,  false), // Front (+Z)
                (ox + d+w + d, oy + d, w,  h,  true),  // Back  (-Z)
            };

            var faceVerts = new Point3D[6][];
            faceVerts[0] = new[] { P(hw, -hh, hd), P(hw, -hh, -hd), P(hw, hh, -hd), P(hw, hh, hd) };
            faceVerts[1] = new[] { P(-hw, -hh, -hd), P(-hw, -hh, hd), P(-hw, hh, hd), P(-hw, hh, -hd) };
            faceVerts[2] = new[] { P(-hw, hh, hd), P(hw, hh, hd), P(hw, hh, -hd), P(-hw, hh, -hd) };
            faceVerts[3] = new[] { P(-hw, -hh, -hd), P(hw, -hh, -hd), P(hw, -hh, hd), P(-hw, -hh, hd) };
            faceVerts[4] = new[] { P(-hw, -hh, hd), P(hw, -hh, hd), P(hw, hh, hd), P(-hw, hh, hd) };
            faceVerts[5] = new[] { P(hw, -hh, -hd), P(-hw, -hh, -hd), P(-hw, hh, -hd), P(hw, hh, -hd) };

            for (int f = 0; f < 6; f++)
            {
                var (ax, ay, aw, ah, flipU) = faceAtlas[f];
                var verts = faceVerts[f];

                var mesh = new MeshGeometry3D();
                foreach (var v in verts)
                    mesh.Positions.Add(new Point3D(
                        v.X + center.X, v.Y + center.Y, v.Z + center.Z));

                mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);

                double u0 = flipU ? 1.0 : 0.0;
                double u1 = flipU ? 0.0 : 1.0;
                mesh.TextureCoordinates.Add(new Point(u0, 1));
                mesh.TextureCoordinates.Add(new Point(u1, 1));
                mesh.TextureCoordinates.Add(new Point(u1, 0));
                mesh.TextureCoordinates.Add(new Point(u0, 0));

                // ── CLAVE: CroppedBitmap del atlas ya escalado, aislado por cara ──
                // Al tener su propia imagen sin vecinos, el bilinear de WPF no puede
                // sangrar hacia pixels de otras regiones del atlas.
                var cropped = new CroppedBitmap(scaledAtlas,
                    new Int32Rect(ax * SCALE, ay * SCALE, aw * SCALE, ah * SCALE));

                var brush = new ImageBrush(cropped)
                {
                    Viewport = new Rect(0, 0, 1, 1),
                    ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                    TileMode = TileMode.None,
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);

                var mat = new DiffuseMaterial(brush);
                group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            }
        }

        /// <summary>
        /// Escala un BitmapSource con nearest-neighbor puro, píxel a píxel.
        /// </summary>
        private static BitmapSource ScaleNearestNeighbor(BitmapSource src, int scale)
        {
            var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int srcW = fmt.PixelWidth;
            int srcH = fmt.PixelHeight;
            int dstW = srcW * scale;
            int dstH = srcH * scale;

            int stride = srcW * 4;
            var srcPixels = new byte[stride * srcH];
            fmt.CopyPixels(srcPixels, stride, 0);

            int dstStride = dstW * 4;
            var dstPixels = new byte[dstStride * dstH];

            for (int sy = 0; sy < srcH; sy++)
                for (int sx = 0; sx < srcW; sx++)
                {
                    int srcIdx = sy * stride + sx * 4;
                    byte b = srcPixels[srcIdx];
                    byte g = srcPixels[srcIdx + 1];
                    byte r = srcPixels[srcIdx + 2];
                    byte a = srcPixels[srcIdx + 3];

                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int dstIdx = (sy * scale + dy) * dstStride + (sx * scale + dx) * 4;
                            dstPixels[dstIdx] = b;
                            dstPixels[dstIdx + 1] = g;
                            dstPixels[dstIdx + 2] = r;
                            dstPixels[dstIdx + 3] = a;
                        }
                }

            return BitmapSource.Create(dstW, dstH, 96, 96,
                PixelFormats.Bgra32, null, dstPixels, dstStride);
        }

        private static Point3D P(double x, double y, double z) => new Point3D(x, y, z);
    }
}
