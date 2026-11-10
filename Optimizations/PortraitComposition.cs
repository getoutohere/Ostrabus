using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Ostrabus.Optimizations;

// Borrow input storage for this synchronous call only. Keep float output until
// Unity performs the same texture conversion as native SetPixel; never quantize
// intermediate layers or retain a texture, CO or borrowed pointer in a cache.
internal static unsafe class PortraitComposition {
    internal struct Layer {
        internal byte* Pixels;
        internal int Stride;
        internal int Red, Alpha;
    }
    const int MaxLayers = 32;
    const int MaxPixels = 262144;
    static readonly float[] normalized = NormalizedChannels();
    [ThreadStatic] static Color[] output;
    [ThreadStatic] static bool busy;

    internal static bool Supports(TextureFormat format) => format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32 || format == TextureFormat.RGB24;
    internal static int BytesPerPixel(TextureFormat format) => format == TextureFormat.RGB24 ? 3 : 4;
    internal static Color[] Buffer(int count) {
        if (output == null || output.Length != count) output = new Color[count];
        return output;
    }
    static float[] NormalizedChannels() {
        var values = new float[256];
        for (int i = 0; i < values.Length; i++) values[i] = ((Color)new Color32((byte)i, 0, 0, 0)).r;
        return values;
    }
    internal static void Blend(Layer* layers, int layerCount, int count, Color[] destination) {
        Color black = Color.black;
        for (int p = 0; p < count; p++) destination[p] = black;
        for (int layer = 0; layer < layerCount; layer++) {
            var source = layers[layer];
            byte* pixel = source.Pixels;
            for (int p = 0; p < count; p++, pixel += source.Stride) {
                int alpha = source.Stride == 3 ? 255 : pixel[source.Alpha];
                // Byte colors are finite and alpha is within [0,1]. Native
                // Lerp at zero leaves the accumulated color unchanged.
                if (alpha == 0) continue;
                float amount = normalized[alpha];
                Color color = destination[p];
                color.r = color.r + (normalized[pixel[source.Red]] - color.r) * amount;
                color.g = color.g + (normalized[pixel[source.Red + 1]] - color.g) * amount;
                color.b = color.b + (normalized[pixel[source.Red + 2]] - color.b) * amount;
                color.a = color.a + (amount - color.a) * amount;
                destination[p] = color;
            }
        }
    }
    internal static void Compose(Texture2D texture, Texture2D[] layers) {
        if (!TryCompose(texture, layers)) NativePixels(texture, layers);
    }
    internal static bool TryCompose(Texture2D texture, Texture2D[] layers) {
        if (busy || texture == null || layers == null || layers.Length == 0 || layers.Length > MaxLayers) return false;
        int width = texture.width, height = texture.height;
        long total = (long)width * height;
        if (width <= 0 || height <= 0 || total > MaxPixels || !texture.isReadable) return false;
        foreach (var layer in layers) {
            if (layer == null || layer.width != width || layer.height != height || !layer.isReadable || !Supports(layer.format)) return false;
        }
        busy = true;
        try {
            int count = (int)total;
            Layer* views = stackalloc Layer[layers.Length];
            for (int i = 0; i < layers.Length; i++) {
                int stride = BytesPerPixel(layers[i].format);
                var bytes = layers[i].GetPixelData<byte>(0);
                if (bytes.Length < (long)count * stride) return false;
                bool argb = layers[i].format == TextureFormat.ARGB32;
                views[i] = new Layer { Pixels = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes), Stride = stride, Red = argb ? 1 : 0, Alpha = argb ? 0 : 3 };
            }
            Color[] pixels = Buffer(count);
            Blend(views, layers.Length, count, pixels);
            GC.KeepAlive(layers);
            texture.SetPixels(pixels);
            return true;
        } finally { busy = false; }
    }
    // Preserve native sampling/wrapping and error behavior for unusual or modded
    // formats, mismatched dimensions, unreadable layers and reentrant callers.
    internal static void NativePixels(Texture2D texture, Texture2D[] layers) {
        int width = layers[2].width, height = layers[2].height;
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                Color color = Color.magenta;
                bool first = true;
                foreach (var layer in layers) {
                    Color pixel = layer.GetPixel(x, y);
                    color = Color.Lerp(first ? Color.black : color, pixel, pixel.a / 1f);
                    first = false;
                }
                texture.SetPixel(x, y, color);
            }
        }
    }
}
