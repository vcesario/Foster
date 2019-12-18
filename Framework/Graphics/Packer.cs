﻿using System;
using System.Collections.Generic;

namespace Foster.Framework
{
    /// <summary>
    /// The Packer takes source image data and packs them into large texture pages that can then be used for Atlases
    /// This is useful for sprite fonts, sprite sheets, etc.
    /// </summary>
    public class Packer
    {

        /// <summary>
        /// A single packed Entry
        /// </summary>
        public class Entry
        {
            /// <summary>
            /// The Name of the Entry
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// The corresponding image page of the Entry
            /// </summary>
            public readonly int Page;

            /// <summary>
            /// The Source Rectangle
            /// </summary>
            public readonly RectInt Source;

            /// <summary>
            /// The Frame Rectangle. This is the size of the image before it was packed
            /// </summary>
            public readonly RectInt Frame;

            public Entry(string name, int page, RectInt source, RectInt frame)
            {
                Name = name;
                Page = page;
                Source = source;
                Frame = frame;
            }
        }

        /// <summary>
        /// Stores the Packed result of the Packer
        /// </summary>
        public class Output
        {
            public readonly List<Bitmap> Pages = new List<Bitmap>();
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        }

        /// <summary>
        /// The Packed Output
        /// This is null if the Packer has not yet been packed
        /// </summary>
        public Output? Packed { get; private set; }

        /// <summary>
        /// Whether the Packer has unpacked source data
        /// </summary>
        public bool HasUnpackedData { get; private set; }

        /// <summary>
        /// Whether to trim transparency from the source images
        /// </summary>
        public bool Trim = true;

        /// <summary>
        /// Max Page Size
        /// </summary>
        public int MaxSize = 8192;

        /// <summary>
        /// Image Padding
        /// </summary>
        public int Padding = 1;

        /// <summary>
        /// Power of Two
        /// </summary>
        public bool PowerOfTwo = false;


        public int SourceImageCount { get; private set; } = 0;

        private class Source
        {
            public string Name;
            public RectInt Packed;
            public RectInt Frame;
            public Color[]? Buffer;
            public bool Empty => Packed.Width <= 0 || Packed.Height <= 0;

            public Source(string name)
            {
                Name = name;
            }
        }

        private readonly List<Source> sources = new List<Source>();

        public Packer()
        {

        }

        public void AddPixels(string name, int width, int height, Span<Color> pixels)
        {
            AddSource(name, width, height, pixels);
        }

        public void AddBitmap(string name, Bitmap bitmap)
        {
            if (bitmap != null)
                AddSource(name, bitmap.Width, bitmap.Height, new Span<Color>(bitmap.Pixels));
        }

        public void AddFile(string name, string path)
        {
            throw new NotImplementedException();
        }

        private void AddSource(string name, int width, int height, Span<Color> pixels)
        {
            HasUnpackedData = true;
            SourceImageCount++;

            var source = new Source(name);
            int top = 0, left = 0, right = width, bottom = height;

            // trim
            if (Trim)
            {
                // TOP:
                for (int y = 0; y < height; y++)
                    for (int x = 0, s = y * width; x < width; x++, s++)
                        if (pixels[s].A > 0)
                        {
                            top = y;
                            goto LEFT;
                        }
                    LEFT:
                for (int x = 0; x < width; x++)
                    for (int y = top, s = x + y * width; y < height; y++, s += width)
                        if (pixels[s].A > 0)
                        {
                            left = x;
                            goto RIGHT;
                        }
                    RIGHT:
                for (int x = width - 1; x >= left; x--)
                    for (int y = top, s = x + y * width; y < height; y++, s += width)
                        if (pixels[s].A > 0)
                        {
                            right = x + 1;
                            goto BOTTOM;
                        }
                    BOTTOM:
                for (int y = height - 1; y >= top; y--)
                    for (int x = left, s = x + y * width; x < right; x++, s++)
                        if (pixels[s].A > 0)
                        {
                            bottom = y + 1;
                            goto END;
                        }
                    END:;
            }

            // determine sizes
            // there's a chance this image was empty in which case we have no width / height
            if (left <= right && top <= bottom)
            {
                source.Packed = new RectInt(0, 0, right - left, bottom - top);
                source.Frame = new RectInt(-left, -top, width, height);
                source.Buffer = new Color[source.Packed.Width * source.Packed.Height];

                // copy our trimmed pixel data to the main buffer
                for (int i = 0; i < source.Packed.Height; i++)
                {
                    var run = source.Packed.Width;
                    var from = pixels.Slice(left + (top + i) * width, run);
                    var to = new Span<Color>(source.Buffer, i * run, run);

                    from.CopyTo(to);
                }
            }
            else
            {
                source.Packed = new RectInt();
                source.Frame = new RectInt(0, 0, width, height);
            }

            sources.Add(source);
        }

        private unsafe struct PackingNode
        {
            public bool Used;
            public RectInt Rect;
            public PackingNode* Right;
            public PackingNode* Down;
        };

        public unsafe Output? Pack()
        {
            // Already been packed
            if (!HasUnpackedData)
                return Packed;

            // Reset
            Packed = new Output();
            HasUnpackedData = false;

            // Nothing to pack
            if (sources.Count <= 0)
                return Packed;

            // sort the sources by size
            sources.Sort((a, b) => b.Packed.Width * b.Packed.Height - a.Packed.Width * a.Packed.Height);

            // make sure the largest isn't too large
            if (sources[0].Packed.Width > MaxSize || sources[0].Packed.Height > MaxSize)
                throw new Exception("Source image is larger than max atlas size");

            // we should never need more nodes than source images * 3
            Span<PackingNode> buffer = (sources.Count <= 1000 ?
                stackalloc PackingNode[sources.Count * 4] :
                new PackingNode[sources.Count * 4]);

            // using pointer operations here was faster
            fixed (PackingNode* nodes = buffer)
            {
                int packed = 0, page = 0;
                while (packed < sources.Count)
                {
                    if (sources[packed].Empty)
                    {
                        packed++;
                        continue;
                    }

                    int from = packed;
                    var index = nodes;
                    var root = ResetNode(index++, 0, 0, sources[from].Packed.Width + Padding, sources[from].Packed.Height + Padding);

                    while (packed < sources.Count)
                    {
                        if (sources[packed].Empty)
                        {
                            packed++;
                            continue;
                        }

                        int w = sources[packed].Packed.Width + Padding;
                        int h = sources[packed].Packed.Height + Padding;
                        var node = FindNode(root, w, h);

                        // try to expand
                        if (node == null)
                        {
                            bool canGrowDown = (w <= root->Rect.Width) && (root->Rect.Height + h < MaxSize);
                            bool canGrowRight = (h <= root->Rect.Height) && (root->Rect.Width + w < MaxSize);
                            bool shouldGrowRight = canGrowRight && (root->Rect.Height >= (root->Rect.Width + w));
                            bool shouldGrowDown = canGrowDown && (root->Rect.Width >= (root->Rect.Height + h));

                            if (canGrowDown || canGrowRight)
                            {
                                // grow right
                                if (shouldGrowRight || (!shouldGrowDown && canGrowRight))
                                {
                                    var next = ResetNode(index++, 0, 0, root->Rect.Width + w, root->Rect.Height);
                                    next->Used = true;
                                    next->Down = root;
                                    next->Right = node = ResetNode(index++, root->Rect.Width, 0, w, root->Rect.Height);
                                    root = next;
                                }
                                // grow down
                                else
                                {
                                    var next = ResetNode(index++, 0, 0, root->Rect.Width, root->Rect.Height + h);
                                    next->Used = true;
                                    next->Down = node = ResetNode(index++, 0, root->Rect.Height, root->Rect.Width, h);
                                    next->Right = root;
                                    root = next;
                                }
                            }
                        }

                        // doesn't fit in this page
                        if (node == null)
                            break;

                        // add
                        node->Used = true;
                        node->Down = ResetNode(index++, node->Rect.X, node->Rect.Y + h, node->Rect.Width, node->Rect.Height - h);
                        node->Right = ResetNode(index++, node->Rect.X + w, node->Rect.Y, node->Rect.Width - w, h);

                        sources[packed].Packed.X = node->Rect.X;
                        sources[packed].Packed.Y = node->Rect.Y;

                        packed++;
                    }

                    // get page size
                    int pageWidth, pageHeight;
                    if (PowerOfTwo)
                    {
                        pageWidth = 2;
                        pageHeight = 2;
                        while (pageWidth < root->Rect.Width)
                            pageWidth *= 2;
                        while (pageHeight < root->Rect.Height)
                            pageHeight *= 2;
                    }
                    else
                    {
                        pageWidth = root->Rect.Width;
                        pageHeight = root->Rect.Height;
                    }

                    // create each page
                    {
                        var bmp = new Bitmap(pageWidth, pageHeight);
                        Packed.Pages.Add(bmp);

                        // create each entry for this page and copy its image data
                        for (int i = from; i < packed; i++)
                        {
                            var source = sources[i];
                            var entry = new Entry(source.Name, page, source.Packed, source.Frame);

                            Packed.Entries.Add(entry.Name, entry);

                            if (!source.Empty)
                                bmp.SetPixels(sources[i].Packed, sources[i].Buffer);
                        }
                    }

                    page++;
                }

            }

            return Packed;

            static unsafe PackingNode* FindNode(PackingNode* root, int w, int h)
            {
                if (root->Used)
                {
                    var r = FindNode(root->Right, w, h);
                    return (r != null ? r : FindNode(root->Down, w, h));
                }
                else if (w <= root->Rect.Width && h <= root->Rect.Height)
                {
                    return root;
                }

                return null;
            }

            static unsafe PackingNode* ResetNode(PackingNode* node, int x, int y, int w, int h)
            {
                node->Used = false;
                node->Rect = new RectInt(x, y, w, h);
                node->Right = null;
                node->Down = null;
                return node;
            }
        }

        /// <summary>
        /// Removes all source data and removes the Packed Output
        /// </summary>
        public void Clear()
        {
            sources.Clear();
            Packed = null;
            SourceImageCount = 0;
            HasUnpackedData = false;
        }

    }
}