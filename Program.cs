using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
                using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath);
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
                
                // If there are files OR directories in the subdirectory, and there's exactly one subdirectory at this level,
                // we should flatten. The old logic only flattened when there was exactly one file and no subs, or when
                // there was one sub with one sub-subdirectory. This missed the case of one subdirectory with files.
                if (filesInSub.Length == 0 && dirsInSub.Length == 0)
                    break;
                
                if (dirsInSub.Length == 1)
                {
                    // Case: single subdirectory that contains another single subdirectory (nested structure)
                    var singleSubDir = dirsInSub[0];
                    if (Directory.Exists(singleSubDir))
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
                }
                else if (filesInSub.Length > 0 || dirsInSub.Length > 0)
                {
                    // Case: subdirectory contains files and/or multiple directories - move everything up
                    foreach (var file in filesInSub)
                        File.Move(file, Path.Combine(directory, Path.GetFileName(file)), true);
                    foreach (var dir in dirsInSub)
                        Directory.Move(dir, Path.Combine(directory, Path.GetFileName(dir)));
                    Directory.Delete(subDir, true);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Flattened {Path.GetFileName(subDir)}");
                    Console.ResetColor();
                    break; // After flattening files up, we're done at this level
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

        static string ComputeSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        static void ReplaceWithDuplicateMarker(string filePath, string originalRelativePath)
        {
            var markerPath = Path.ChangeExtension(filePath, ".txt");
            var content = "identical to file: " + originalRelativePath;
            File.WriteAllText(markerPath, content);
            try { File.Delete(filePath); }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Warning: Could not delete duplicate '{filePath}': {ex.Message}");
                Console.ResetColor();
            }
        }

        static int RemoveDuplicates(string directory)
        {
            var allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(f => new { Path = f, Size = new FileInfo(f).Length })
                .ToList();
            
            if (allFiles.Count == 0)
                return 0;

            // Group by size first - files with different sizes can't be duplicates
            var filesBySize = allFiles.GroupBy(f => f.Size)
                .Where(g => g.Count() > 1) // Only consider sizes with multiple files
                .ToDictionary(g => g.Key, g => g.ToList());
            
            if (filesBySize.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  No duplicates found.");
                Console.ResetColor();
                return 0;
            }

            var hashToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<(string duplicate, string original)>();
            int filesProcessed = 0;
            int totalFilesToProcess = filesBySize.Sum(g => g.Value.Count);
            int duplicateCount = 0;

            Console.WriteLine($"  Checking {totalFilesToProcess} files for duplicates...");
            
            foreach (var sizeGroup in filesBySize.Values)
            {
                foreach (var fileInfo in sizeGroup)
                {
                    try
                    {
                        filesProcessed++;
                        if (filesProcessed % 10 == 0)
                        {
                            Console.WriteLine($"  Processing duplicates: {filesProcessed}/{totalFilesToProcess}...");
                        }
                        var hash = ComputeSha256(fileInfo.Path);
                        if (hashToFile.TryGetValue(hash, out var existingFile))
                        {
                            duplicates.Add((fileInfo.Path, existingFile));
                        }
                        else
                        {
                            hashToFile[hash] = fileInfo.Path;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  Warning: Could not compute hash for '{fileInfo.Path}': {ex.Message}");
                        Console.ResetColor();
                    }
                }
            }

            if (duplicates.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  No duplicates found.");
                Console.ResetColor();
                return 0;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Found {duplicates.Count} duplicate(s).");
            Console.ResetColor();

            var filesToKeep = new HashSet<string>(allFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var filesToReplace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (dup, orig) in duplicates)
            {
                if (filesToReplace.Contains(dup) || filesToReplace.Contains(orig))
                    continue;

                if (filesToKeep.Contains(orig))
                {
                    filesToReplace.Add(dup);
                }
                else if (filesToKeep.Contains(dup))
                {
                    filesToReplace.Add(orig);
                }
            }

            foreach (var fileToReplace in filesToReplace)
            {
                var original = duplicates.First(d => d.duplicate == fileToReplace || d.original == fileToReplace);
                var keepFile = original.duplicate == fileToReplace ? original.original : original.duplicate;
                
                var relativePath = Path.GetRelativePath(directory, keepFile);
                ReplaceWithDuplicateMarker(fileToReplace, relativePath);
                duplicateCount++;
                Console.WriteLine($"  Replaced duplicate: '{Path.GetRelativePath(directory, fileToReplace)}' -> identical to '{relativePath}'");
            }

            return duplicateCount;
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
                    RemoveDuplicates(destDir);
                }
                else
                    Console.WriteLine("  Failed to extract or already exists");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nDone.");
            Console.ResetColor();
            
            // Only wait for keypress if running interactively (not redirected/automated)
            if (Console.IsInputRedirected == false)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
        }
    }
}
