using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace DirectorySynchronization
{
    class Program
    {
        public class FolderElement : IEquatable<FolderElement>
        {
            // only name with extension(if it's file) without path
            public string Name;
            public string Path;
            public bool IsDirectory;
            public long Size = 0;
            public DateTime LastWriteTime = DateTime.MinValue;

            public bool Equals(FolderElement other)
            {
                return other != null &&
                       Name == other.Name &&
                       Path == other.Path &&
                       IsDirectory == other.IsDirectory;
            }

            public override int GetHashCode()
            {
                int hashName = Name == null ? 0 : Name.GetHashCode();
                int hashPath = Path == null ? 0 : Path.GetHashCode();
                int hashIsDirectory = IsDirectory.GetHashCode();
                int hashSize = Size.GetHashCode();
                int hashLastWriteTime = LastWriteTime.GetHashCode();

                return hashName ^ hashPath ^ hashIsDirectory ^ hashSize ^ hashLastWriteTime;
            }
        }

        public class NameAndPathComparer : IEqualityComparer<FolderElement>
        {
            public bool Equals(FolderElement x, FolderElement y)
            {
                return x != null &&
                       y != null &&
                       x.Name == y.Name &&
                       x.Path == y.Path &&
                       x.IsDirectory == y.IsDirectory;
            }

            public int GetHashCode([DisallowNull] FolderElement obj)
            {
                int hashName = obj.Name == null ? 0 : obj.Name.GetHashCode();
                int hashPath = obj.Path == null ? 0 : obj.Path.GetHashCode();
                int hashIsDirectory = obj.IsDirectory.GetHashCode();

                return hashName ^ hashPath ^ hashIsDirectory;
            }
        }

        public class FileComparer : IEqualityComparer<FolderElement>
        {
            public bool Equals(FolderElement x, FolderElement y)
            {
                return x != null &&
                       y != null &&
                       x.Name == y.Name &&
                       x.Path == y.Path &&
                       x.IsDirectory == y.IsDirectory &&
                       x.Size == y.Size &&
                       x.LastWriteTime == y.LastWriteTime;
            }

            public int GetHashCode([DisallowNull] FolderElement obj)
            {
                int hashName = obj.Name == null ? 0 : obj.Name.GetHashCode();
                int hashPath = obj.Path == null ? 0 : obj.Path.GetHashCode();
                int hashIsDirectory = obj.IsDirectory.GetHashCode();
                int hashSize = obj.Size.GetHashCode();
                int hashLastWriteTime = obj.LastWriteTime.GetHashCode();

                return hashName ^ hashPath ^ hashIsDirectory ^ hashSize ^ hashLastWriteTime;
            }
        }

        static void WriteLog(string operation, string message)
        {
            string text = operation + ": " + message;

            Console.WriteLine(text);
            System.IO.File.AppendAllText(@"d:\log.txt", text + Environment.NewLine);
        }

        static HashSet<FolderElement> SearchDirectory(string basePath, string path)
        {
            HashSet<FolderElement> folderElements = new HashSet<FolderElement>();

            string[] files = null;
            string[] directories = null;

            string searchPath = basePath + path;

            try
            {
                directories = Directory.GetDirectories(searchPath);

                try
                {
                    files = Directory.GetFiles(searchPath, "*.*");
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Search_File_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Search_File_PathTooLongException", ex.Message);
                    }
                }

            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    WriteLog("Err_Search_Dir_UnauthorizedAccessException", ex.Message);
                }
                if (ex is PathTooLongException)
                {
                    WriteLog("Err_Search_Dir_PathTooLongException", ex.Message);
                }
            }


            if (files != null)
            {
                foreach (string file in files)
                {
                    FolderElement folderElement = new FolderElement();

                    FileInfo fileInfo = new FileInfo(file);

                    folderElement.Name = fileInfo.Name;
                    folderElement.Path = path;
                    folderElement.IsDirectory = false;
                    folderElement.Size = fileInfo.Length;
                    folderElement.LastWriteTime = fileInfo.LastWriteTime;

                    folderElements.Add(folderElement);

                    //WriteLog("List_File", file);
                }
            }

            if (directories != null)
            {
                foreach (string dir in directories)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dir);

                    FolderElement folderElement = new FolderElement();

                    folderElement.Name = dirInfo.Name;
                    folderElement.Path = path;
                    folderElement.IsDirectory = true;

                    folderElements.Add(folderElement);

                    //WriteLog("List_Dir", dir);

                    folderElements.UnionWith(SearchDirectory(basePath, path + dirInfo.Name + @"\"));
                    //folderElements.AddRange(SearchDirectory(basePath, path + dirInfo.Name + @"\"));
                }
            }

            return (folderElements);
        }

        static string CalculateMD5ForFile(string fileName)
        {
            MD5 md5 = MD5.Create();

            FileStream stream = File.OpenRead(fileName);
            byte[] hash = md5.ComputeHash(stream);
            
            return (BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
        }

        static bool FilesBytesContentCompare(string sourceFileName, string destinationFileName)
        {
            int sourceFilebyte;
            int destinationFilebyte;
            FileStream sourceFileStream;
            FileStream destinationFileStream;

            sourceFileStream = new FileStream(sourceFileName, FileMode.Open);
            destinationFileStream = new FileStream(destinationFileName, FileMode.Open);

            bool result = true;
            // if by going thru there are two different bytes at same possition cyclus ends and files are not the same
            do
            {
                sourceFilebyte = sourceFileStream.ReadByte();
                destinationFilebyte = destinationFileStream.ReadByte();

                if (sourceFilebyte != destinationFilebyte) { result = false; }
            }
            while ((sourceFilebyte == destinationFilebyte) && (sourceFilebyte != -1));

            sourceFileStream.Close();
            destinationFileStream.Close();

            return result;
        }


        static void UpdateDirectory(string sourcePath, string destinationPath, int comparativeMethod)
        {
            HashSet<FolderElement> sourceFolderElements = SearchDirectory(sourcePath, @"\");
            HashSet<FolderElement> destinationFolderElements = SearchDirectory(destinationPath, @"\");


            HashSet<FolderElement> filesToDelete = new();
            filesToDelete = destinationFolderElements.Except(sourceFolderElements, new NameAndPathComparer()).Where(f => f.IsDirectory == false).ToHashSet();

            // delete all files from destination if they dont't exist in source structure
            foreach (FolderElement fileTD in filesToDelete)
            {
                try
                {
                    System.IO.File.Delete(destinationPath + fileTD.Path + fileTD.Name);

                    WriteLog("Delete_File", destinationPath + fileTD.Path + fileTD.Name);
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Delete_File_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Delete_File_PathTooLongException", ex.Message);
                    }
                    if (ex is ArgumentNullException)
                    {
                        WriteLog("Err_Delete_File_ArgumentNullException", ex.Message);
                    }
                    if (ex is DirectoryNotFoundException)
                    {
                        WriteLog("Err_Delete_File_DirectoryNotFoundException", ex.Message);
                    }
                }
            }


            HashSet<FolderElement> directoriesToDelete = new();
            directoriesToDelete = destinationFolderElements.Except(sourceFolderElements, new NameAndPathComparer()).Where(f => f.IsDirectory == true).ToHashSet();

            // delete all destination directories and subdirectories with files if they dont't exist in source structure
            foreach (FolderElement directoryTD in directoriesToDelete)
            {
                try
                {
                    System.IO.Directory.Delete(destinationPath + directoryTD.Path + directoryTD.Name, true);
                    WriteLog("Delete_Dir", destinationPath + directoryTD.Path + directoryTD.Name);
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Delete_Dir_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Delete_Dir_PathTooLongException", ex.Message);
                    }
                    if (ex is ArgumentNullException)
                    {
                        WriteLog("Err_Delete_Dir_ArgumentNullException", ex.Message);
                    }
                    if (ex is DirectoryNotFoundException)
                    {
                        WriteLog("Err_Delete_Dir_DirectoryNotFoundException", ex.Message);
                    }
                }
            }


            HashSet<FolderElement> directoriesToCreate = new();
            directoriesToCreate = sourceFolderElements.Except(destinationFolderElements, new NameAndPathComparer()).Where(f => f.IsDirectory == true).ToHashSet();

            // create all destination directories and subdirectories if they dont't exist
            foreach (FolderElement directoryTC in directoriesToCreate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(destinationPath + directoryTC.Path + directoryTC.Name);
                    WriteLog("Create_Dir", destinationPath + directoryTC.Path + directoryTC.Name);
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Create_Dir_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Create_Dir_PathTooLongException", ex.Message);
                    }
                    if (ex is ArgumentNullException)
                    {
                        WriteLog("Err_Create_Dir_ArgumentNullException", ex.Message);
                    }
                    if (ex is DirectoryNotFoundException)
                    {
                        WriteLog("Err_Create_Dir_DirectoryNotFoundException", ex.Message);
                    }
                }

            }


            HashSet<FolderElement> filesToCopy = new();
            filesToCopy = sourceFolderElements.Except(destinationFolderElements, new FileComparer()).Where(f => f.IsDirectory == false).ToHashSet();

            // copy new files to destination directories if they dont't exist they are not same (date, size, content)
            foreach (FolderElement filesTC in filesToCopy)
            {
                try
                {
                    System.IO.File.Copy(sourcePath + filesTC.Path + filesTC.Name, destinationPath + filesTC.Path + filesTC.Name, true);
                    WriteLog("Copy_File", destinationPath + filesTC.Path + filesTC.Name);
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Copy_File_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Copy_File_PathTooLongException", ex.Message);
                    }
                    if (ex is ArgumentNullException)
                    {
                        WriteLog("Err_Copy_File_ArgumentNullException", ex.Message);
                    }
                    if (ex is DirectoryNotFoundException)
                    {
                        WriteLog("Err_Copy_File_DirectoryNotFoundException", ex.Message);
                    }
                }
            }

            // check files that exist in source and destination and have same last access time and size for content
            HashSet<FolderElement> filesToCheckContent = new();
            filesToCheckContent = sourceFolderElements.Intersect(destinationFolderElements, new FileComparer()).Where(f => f.IsDirectory == false).ToHashSet();
            foreach (FolderElement filesTCC in filesToCheckContent)
            {
                try
                {
                    bool result = true;

                    switch (comparativeMethod)
                    {
                        case 1:
                            result = FilesBytesContentCompare(sourcePath + filesTCC.Path + filesTCC.Name, destinationPath + filesTCC.Path + filesTCC.Name);

                            break;

                        case 2:
                            string sourceMD5 = CalculateMD5ForFile(sourcePath + filesTCC.Path + filesTCC.Name);
                            string destinationMD5 = CalculateMD5ForFile(destinationPath + filesTCC.Path + filesTCC.Name);
                            if (!sourceMD5.Equals(destinationMD5)) { result = false; }
                            
                            break;
                    }

                    if (!result)
                    {
                        System.IO.File.Copy(sourcePath + filesTCC.Path + filesTCC.Name, destinationPath + filesTCC.Path + filesTCC.Name, true);
                        WriteLog("Copy_File", destinationPath + filesTCC.Path + filesTCC.Name);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is UnauthorizedAccessException)
                    {
                        WriteLog("Err_Copy_File_UnauthorizedAccessException", ex.Message);
                    }
                    if (ex is PathTooLongException)
                    {
                        WriteLog("Err_Copy_File_PathTooLongException", ex.Message);
                    }
                    if (ex is ArgumentNullException)
                    {
                        WriteLog("Err_Copy_File_ArgumentNullException", ex.Message);
                    }
                    if (ex is DirectoryNotFoundException)
                    {
                        WriteLog("Err_Copy_File_DirectoryNotFoundException", ex.Message);
                    }
                }
            }
        }


        static void Main(string[] args)
        {
            // dohliadni na to aby cesta nekoncila lomitkom
            // comparativeMethod: 1 - MD5, 2 - ByteToByte


            WriteLog("Synchronization_Start", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss"));

            UpdateDirectory(@"D:\x_test_folder\source", @"D:\x_test_folder\dest", 2);

            WriteLog("Synchronization_Finish", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") + "\n----------------------------");

        }
    }
}
