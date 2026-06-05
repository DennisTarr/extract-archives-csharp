using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ExtractArchives
{
    class Program
    {
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "zip", "tar", "tar.gz", "tgz", "tar.bz2", "tbz2",
            "tar.xz", "txz", "tar.lz", "tlz", "tar.lzma", "tar.zst",
            "7z", "rar", "lz", "lzma", "zst", "cbz", "cpak"
        };

        private static readonly HashSet<string> CleanupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".lys", ".url"
        };

        private static readonly HashSet<string> CleanupDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "lys", ".DS_Store", "__MACOSX"
        };

        static bool TryExtract(string archivePath, string destinationDir)
        {
            if (string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(destinationDir))
                return false;

            var ext = Path.GetExtension(archivePath).TrimStart('.');
            if (!ArchiveExtensions.Contains(ext))
                return false;

            if (Directory.Exists(destinationDir))
                Directory.Delete(destinationDir, true);

            try
            {
                Directory.CreateDirectory(destinationDir);
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(destinationDir, new ExtractionOptions()
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
                var files = Directory.GetFiles(destinationDir, "*", SearchOption.AllDirectories);
                return files.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static void FlattenRecursive(string directory)
        {
            do
            {
                var contents = Directory.GetFileSystemEntries(directory);
                if (contents.Length != 1)
                    break;
                var subDir = contents[0];
                if (!Directory.Exists(subDir))
                    break;
                var filesInSub = Directory.GetFiles(subDir);
                var dirsInSub = Directory.GetDirectories(subDir);
                if (dirsInSub.Length > 0 || filesInSub.Length != 1)
                    break;
                var singleSubDir = dirsInSub.Length == 1 ? dirsInSub[0] : null;
                if (singleSubDir != null && Directory.Exists(singleSubDir))
                {
                    foreach (var file in Directory.GetFiles(singleSubDir))
                        File.Move(file, Path.Combine(subDir, Path.GetFileName(file)), true);
                    foreach (var dir in Directory.GetDirectories(singleSubDir))
                        Directory.Move(dir, Path.Combine(subDir, Path.GetFileName(dir)));
                    Directory.Delete(singleSubDir, true);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Flattened {Path.GetFileName(singleSubDir)}");
                    Console.ResetColor();
                }
            } while (true);
        }

        static void FlattenAll(string directory)
        {
            var directories = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(directories);
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                    continue;
                var contents = Directory.GetFileSystemEntries(dir);
                if (contents.Length == 1 && Directory.Exists(contents[0]))
                {
                    var subDir = contents[0];
                    foreach (var file in Directory.GetFiles(subDir))
                        File.Move(file, Path.Combine(dir, Path.GetFileName(file)), true);
                    foreach (var sub in Directory.GetDirectories(subDir))
                        Directory.Move(sub, Path.Combine(dir, Path.GetFileName(sub)));
                    Directory.Delete(subDir, true);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Flattened {Path.GetFileName(subDir)}");
                    Console.ResetColor();
                }
            }
            // Also check the root directory itself
            var rootContents = Directory.GetFileSystemEntries(directory);
            if (rootContents.Length == 1 && Directory.Exists(rootContents[0]))
            {
                var subDir = rootContents[0];
                foreach (var file in Directory.GetFiles(subDir))
                    File.Move(file, Path.Combine(directory, Path.GetFileName(file)), true);
                foreach (var sub in Directory.GetDirectories(subDir))
                    Directory.Move(sub, Path.Combine(directory, Path.GetFileName(sub)));
                Directory.Delete(subDir, true);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  Flattened {Path.GetFileName(subDir)}");
                Console.ResetColor();
            }
        }

        static void CleanupSafe(string directory)
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (CleanupExtensions.Contains(ext) || Path.GetFileName(file).Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(file); } catch { }
            }
            var dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories);
            foreach (var dir in dirs)
            {
                var dirName = Path.GetFileName(dir);
                if (CleanupDirectories.Contains(dirName))
                    try { Directory.Delete(dir, true); } catch { }
            }
        }

        static void Main(string[] args)
        {
            var currentDir = Directory.GetCurrentDirectory();
            var files = Directory.GetFiles(currentDir);
            var archives = new List<string>();
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).TrimStart('.');
                if (ArchiveExtensions.Contains(ext))
                    archives.Add(file);
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Found {archives.Count} archive(s)");
            Console.ResetColor();
            foreach (var archivePath in archives)
            {
                var fileName = Path.GetFileNameWithoutExtension(archivePath);
                var baseName = fileName;
                var ext = Path.GetExtension(archivePath).TrimStart('.');
                while (ArchiveExtensions.Contains(ext))
                {
                    baseName = Path.GetFileNameWithoutExtension(baseName);
                    var newExt = Path.GetExtension(baseName).TrimStart('.');
                    if (string.IsNullOrEmpty(newExt))
                        break;
                    ext = newExt;
                }
                var destDir = Path.Combine(currentDir, baseName);
                Console.WriteLine($"Extracting '{Path.GetFileName(archivePath)}' -> '{destDir}/'");
                if (TryExtract(archivePath, destDir))
                {
                    FlattenRecursive(destDir);
                    CleanupSafe(destDir);
                    var nestedFiles = Directory.GetFiles(destDir, "*", SearchOption.AllDirectories);
                    foreach (var nestedFile in nestedFiles)
                    {
                        var nestedExt = Path.GetExtension(nestedFile).TrimStart('.');
                        if (ArchiveExtensions.Contains(nestedExt))
                        {
                            var nestedDest = Path.Combine(Path.GetDirectoryName(nestedFile)!, Path.GetFileNameWithoutExtension(nestedFile));
                            Console.WriteLine($"    Extracting nested: {nestedFile}");
                            if (TryExtract(nestedFile, nestedDest))
                            {
                                FlattenRecursive(nestedDest);
                                CleanupSafe(nestedDest);
                                File.Delete(nestedFile);
                            }
                            else
                                Console.WriteLine($"    Failed to extract {nestedFile}");
                        }
                    }
                    FlattenAll(destDir);
                    CleanupSafe(destDir);
                }
                else
                    Console.WriteLine("  Failed to extract or already exists");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nDone. Press any key to exit...");
            Console.ResetColor();
            Console.ReadKey(true);
        }
    }
}
