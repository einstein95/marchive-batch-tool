﻿// SPDX-License-Identifier: GPL-3.0-or-later
/*
 * GMWare.M2: Library for manipulating files in formats created by M2 Co., Ltd.
 * Copyright (C) 2019  Yukai Li
 * 
 * This file is part of GMWare.M2.
 * 
 * GMWare.M2 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * GMWare.M2 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with GMWare.M2.  If not, see <https://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Newtonsoft.Json.Linq;
using GMWare.M2.Psb;
using GMWare.M2.Models;

namespace GMWare.M2.MArchive
{
    /// <summary>
    /// A collection of functions for manipulating archive files.
    /// </summary>
    public static class AllDataPacker
    {
        static readonly int ALIGNMENT = 2048;

        /// <summary>
        /// Unpacks an archive file.
        /// </summary>
        /// <param name="psbPath">The path of archive .psb. Can also be .bin or .psb.m.</param>
        /// <param name="outputPath">The path to write unpacked files.</param>
        /// <param name="maPacker">Optional <see cref="MArchivePacker"/> if unpacking .psb.m.</param>
        /// <param name="filter">The <see cref="IPsbFilter"/> to use to decode the PSB file.</param>
        /// <exception cref="ArgumentNullException">
        /// If .psb.m is provided in <paramref name="psbPath"/> but <paramref name="maPacker"/>
        /// is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidDataException">If PSB file does not represent an archive.</exception>
        public static void UnpackFiles(string psbPath, string outputPath, MArchivePacker maPacker = null, IPsbFilter filter = null)
        {
            // Figure out what file we've been given
            if (Path.GetExtension(psbPath).ToLower() == ".bin")
                psbPath = Path.ChangeExtension(psbPath, ".psb");

            // No PSB, try .psb.m
            if (!File.Exists(psbPath)) psbPath += ".m";

            if (Path.GetExtension(psbPath).ToLower() == ".m")
            {
                // Decompress .m file
                if (maPacker == null) throw new ArgumentNullException(nameof(maPacker));
                maPacker.DecompressFile(psbPath, true);
                psbPath = Path.ChangeExtension(psbPath, null);
            }

            ArchiveV1 arch;
            using (FileStream fs = File.OpenRead(psbPath))
            using (PsbReader reader = new PsbReader(fs, filter))
            {
                var root = reader.Root;
                arch = root.ToObject<ArchiveV1>();
            }

            if (arch.ObjectType != "archive" || arch.Version != 1.0)
                throw new InvalidDataException("PSB file is not an archive.");

            using (FileStream fs = File.OpenRead(Path.ChangeExtension(psbPath, ".bin")))
            {
                foreach (var file in arch.FileInfo)
                {
                    Console.WriteLine($"Extracting {file.Key}");
                    string outPath = Path.Combine(outputPath, file.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    using (FileStream ofs = File.Create(outPath))
                    {
                        fs.Seek(file.Value[0], SeekOrigin.Begin);
                        Utils.CopyStream(fs, ofs, file.Value[1]);
                    }
                }
            }
        }

        /// <summary>
        /// Builds an archive file.
        /// </summary>
        /// <param name="folderPath">The directory to make an archive from.</param>
        /// <param name="outputPath">The path of the resulting archive file. Do not include file extension.</param>
        /// <param name="maPacker">Optional <see cref="MArchivePacker"/> if archive PSB is to be compressed.</param>
        /// <param name="filter">The <see cref="IPsbFilter"/> to use to encode the PSB file.</param>
        public static void Build(string folderPath, string outputPath, MArchivePacker maPacker = null, IPsbFilter filter = null)
        {
            using (FileStream packStream = File.Create(outputPath + ".bin"))
            using (FileStream psbStream = File.Create(outputPath + ".psb"))
            {
                ArchiveV1 archive = new ArchiveV1();
                archive.ObjectType = "archive";
                archive.Version = 1.0f;
                archive.FileInfo = new Dictionary<string, List<long>>();
                folderPath = Path.GetFullPath(folderPath);

                foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string key = file.Replace(folderPath, string.Empty).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    Console.WriteLine($"Packing {key}");
                    var currPos = packStream.Position;
                    using (FileStream fs = File.OpenRead(file))
                    {
                        fs.CopyTo(packStream);
                        archive.FileInfo.Add(key.Replace('\\', '/'), new List<long>() { currPos, fs.Length });
                    }
                    var targetLength = (packStream.Position + ALIGNMENT - 1) / ALIGNMENT * ALIGNMENT;
                    byte[] alignBytes = new byte[targetLength - packStream.Position];
                    packStream.Write(alignBytes, 0, alignBytes.Length);
                }

                JToken root = JToken.FromObject(archive);
                PsbWriter writer = new PsbWriter(root, null) { Version = 3 };
                writer.Write(psbStream, filter);
            }

            if (maPacker != null)
            {
                maPacker.CompressFile(outputPath + ".psb");
            }
        }
    }
}
