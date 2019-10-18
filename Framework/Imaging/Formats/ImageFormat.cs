﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Foster.Framework
{
    public abstract class ImageFormat
    {

        public readonly string Name;

        public ImageFormat(string name)
        {
            Name = name;
        }

        public abstract bool IsValid(Stream stream);
        public abstract bool Read(Stream stream, out int width, out int height, out Color[] pixels);
        public abstract bool Write(Stream stream, int width, int height, Color[] pixels);

        public static ImageFormat Png = new PngFormat();

        public static List<ImageFormat> Formats = new List<ImageFormat>()
        {
            Png
        };

    }
}