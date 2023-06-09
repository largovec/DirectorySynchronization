﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Timers;

namespace DirectorySynchronization
{
    class Program
    {
        private static System.Timers.Timer aTimer;
        
        //global variable to carry all command line arguments. Even optional one.
        static ComandLineArgs ComandlineArguments = new();

        // all information about file and directory necessary for copy delete decisions
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

        // HashSet comparer for basic comparation without file size and last write time
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

        // HashSet comparer for detailed comparation with file size and last write time
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

        // for storage all command line argument with predefined values. Event for not mandatory command line parameters
        public class ComandLineArgs
        {
            public string sourcePath;
            public string destinationPath;
            public string logPath;
            public int syncInterval;
            public int comparativeMethod;
            public bool verboseLogging;
            public string errorMessage;

            public ComandLineArgs()
            {
                sourcePath = "";
                destinationPath = "";
                logPath = System.IO.Directory.GetCurrentDirectory();
                if (!logPath.EndsWith(@"\")) { logPath = logPath + @"\"; }
                syncInterval = 60;
                comparativeMethod = 1;
                verboseLogging = false;
                errorMessage = "";
            }
        }

        // write log for console and file.
        // !! Name of log file is predefined and cannot be changed with command line parameters.
        static void WriteLog(string operation, string message)
        {
            string text = operation + ": " + message;

            Console.WriteLine(text);
            System.IO.File.AppendAllText(ComandlineArguments.logPath + "log.txt", text + Environment.NewLine);
        }

        // recursivelly search thru directory and store all informations about files and directories for later comparation
        // it's used for both source and destination directory
        // it handle exceptions for: UnauthorizedAccessException and  PathTooLongException
        //  - these exceptions are logging so user see for example if there are all access right set correctly for all subdirectories
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

                    if (ComandlineArguments.verboseLogging == true) { WriteLog("List_File", file); }
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

                    if (ComandlineArguments.verboseLogging == true) { WriteLog("List_Dir", dir); }

                    folderElements.UnionWith(SearchDirectory(basePath, path + dirInfo.Name + @"\"));
                }
            }

            return (folderElements);
        }

        // calculate MD5 for all files that are requested for detail comparation
        static string CalculateMD5ForFile(string fileName)
        {
            MD5 md5 = MD5.Create();

            FileStream stream = File.OpenRead(fileName);
            byte[] hash = md5.ComputeHash(stream);
            
            return (BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
        }

        // this is just test function for comparation how long would took to synchronize directories with byte to byte check
        // can be choosed as parameter from command line option "-c BYTES"
        static bool FilesBytesContentCompare(string sourceFileName, string destinationFileName)
        {
            int sourceFilebyte;
            int destinationFilebyte;
            FileStream sourceFileStream;
            FileStream destinationFileStream;

            sourceFileStream = new FileStream(sourceFileName, FileMode.Open);
            destinationFileStream = new FileStream(destinationFileName, FileMode.Open);

            bool result = true;
            // if by going thru there are two different bytes at same possition cicle ends and files are not the same
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

        // main function for comparation source and destination directories
        // it's using HashSet for processing all informations about files and folders
        // - because it's fast for comparation
        // - hash code is unique for directory \\path\\name
        // - and also for file \\path\\name\\ [size, last access time]
        // this function contain 6 logical steps:
        //
        // 1. retrieve data for all source and destination directories and files
        // 2. delete all files from destination if they dont't exist in source structure
        //    - NOTE: part of those files could be removed even by step 3. but that would require additional code for log those files
        //            because they would be deleted by System.IO.Directory.Delete( , true) and there is no log output
        // 3. delete all destination directories and subdirectories
        //    - also with files if there are left any form step 2.
        // 4. create all destination directories and subdirectories if they dont't exist
        // 5. copy new files to destination directories if they dont't exist they are not same (date, size, content)
        // 6. check files that exist in source and destination and have same last access time and size for content and if there are diffrences then copy only those
        // 
        // and also handle most common exception in this process with logging 
        static void UpdateDirectory(ComandLineArgs comandLineArgs)
        {
            // 1. retrieve data for all source and destination directories and files
            HashSet<FolderElement> sourceFolderElements = SearchDirectory(comandLineArgs.sourcePath, @"\");
            HashSet<FolderElement> destinationFolderElements = SearchDirectory(comandLineArgs.destinationPath, @"\");


            HashSet<FolderElement> filesToDelete = new();
            filesToDelete = destinationFolderElements.Except(sourceFolderElements, new NameAndPathComparer()).Where(f => f.IsDirectory == false).ToHashSet();

            // 2. delete all files from destination if they dont't exist in source structure
            foreach (FolderElement fileTD in filesToDelete)
            {
                try
                {
                    System.IO.File.Delete(comandLineArgs.destinationPath + fileTD.Path + fileTD.Name);

                    WriteLog("Delete_File", comandLineArgs.destinationPath + fileTD.Path + fileTD.Name);
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

            // 3. delete all destination directories and subdirectories with files if they dont't exist in source structure
            foreach (FolderElement directoryTD in directoriesToDelete)
            {
                try
                {
                    System.IO.Directory.Delete(comandLineArgs.destinationPath + directoryTD.Path + directoryTD.Name, true);
                    WriteLog("Delete_Dir", comandLineArgs.destinationPath + directoryTD.Path + directoryTD.Name);
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

            // 4. create all destination directories and subdirectories if they dont't exist
            foreach (FolderElement directoryTC in directoriesToCreate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(comandLineArgs.destinationPath + directoryTC.Path + directoryTC.Name);
                    WriteLog("Create_Dir", comandLineArgs.destinationPath + directoryTC.Path + directoryTC.Name);
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

            // 5. copy new files to destination directories if they dont't exist they are not same (date, size, content)
            foreach (FolderElement filesTC in filesToCopy)
            {
                try
                {
                    System.IO.File.Copy(comandLineArgs.sourcePath + filesTC.Path + filesTC.Name, comandLineArgs.destinationPath + filesTC.Path + filesTC.Name, true);
                    WriteLog("Copy_File", comandLineArgs.destinationPath + filesTC.Path + filesTC.Name);
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

            // 6. check files that exist in source and destination and have same last access time and size for content and if there are diffrences then copy only those
            HashSet<FolderElement> filesToCheckContent = new();
            filesToCheckContent = sourceFolderElements.Intersect(destinationFolderElements, new FileComparer()).Where(f => f.IsDirectory == false).ToHashSet();
            foreach (FolderElement filesTCC in filesToCheckContent)
            {
                try
                {
                    bool result = true;

                    switch (comandLineArgs.comparativeMethod)
                    {
                        case 1:
                            result = FilesBytesContentCompare(comandLineArgs.sourcePath + filesTCC.Path + filesTCC.Name, comandLineArgs.destinationPath + filesTCC.Path + filesTCC.Name);

                            break;

                        case 2:
                            string sourceMD5 = CalculateMD5ForFile(comandLineArgs.sourcePath + filesTCC.Path + filesTCC.Name);
                            string destinationMD5 = CalculateMD5ForFile(comandLineArgs.destinationPath + filesTCC.Path + filesTCC.Name);
                            if (!sourceMD5.Equals(destinationMD5)) { result = false; }
                            
                            break;
                    }

                    if (!result)
                    {
                        System.IO.File.Copy(comandLineArgs.sourcePath + filesTCC.Path + filesTCC.Name, comandLineArgs.destinationPath + filesTCC.Path + filesTCC.Name, true);
                        WriteLog("Copy_File", comandLineArgs.destinationPath + filesTCC.Path + filesTCC.Name);
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

        // display help for console when app is started without parameters or there are some error with processing parameters
        // all infomations are below in string[] help
        static void DisplayHelp()
        {
            string[] help = {"Syntax:",
                             "  DirectorySynchronization \"sourcePath\" \"destinationPath\" [-ld, -i, -c, -v, -?, -help]",
                             "  sourcePath      - Source directory. Full path is required.",
                             "  destinationPath - Destination directory. Full path is required.",
                             "",
                             "  Additional not mandatory parameters:",
                             "  -ld \"logPath\" - Log directory. Full path is required.",
                             "  -i minutes      - Synchronization repeat interval. Default '60' minutes.",
                             "  -c MD5, BYTES   - Comparation method used to check content of files. Default is MD5.",
                             "  -v NO, YES      - Verbose logging. Default is NO.",
                             "  -?, -help       - Display help.",
                             "  Examples:",
                            @"  DirectorySynchronization C:\SourceDir\ C:\DestinationDir\",
                            @"  DirectorySynchronization 'C:\User data\' C:\DestinationDir\ -ld C:\Log\ -i 60 -c BYTES -v YES"
                            };

            Console.WriteLine("\n\n- - - - - - - H E L P - - - - - - -\n\n");
            foreach (string line in help)
            {
                Console.WriteLine(line + "\n");
            }
            Console.WriteLine("\n\n- - - - - - - H E L P - - - - - - -");
        }

        // parse all command line parameters and if there are some problems return error message with information for user
        // parameters are:
        //  arg[0]      - sourcePath
        //  arg[1]      - destinationPath
        //  arg[#i] -ld - logPath
        //  arg[#i] -i  - syncInterval  
        //  arg[#i] -c  - comparativeMethod
        //  arg[#i] -v  - verboseLogging
        static ComandLineArgs ProcessArguments(string[] args)
        {
            ComandLineArgs comandLineArgs = new();

            comandLineArgs.sourcePath = args[0];
            if (!comandLineArgs.sourcePath.EndsWith(@"\")) { comandLineArgs.sourcePath = comandLineArgs.sourcePath + @"\"; }
            if (!Directory.Exists(comandLineArgs.sourcePath)) { comandLineArgs.errorMessage = comandLineArgs.errorMessage + "Source path don't exist!\n"; }

            comandLineArgs.destinationPath = args[1];
            if (!comandLineArgs.destinationPath.EndsWith(@"\")) { comandLineArgs.destinationPath = comandLineArgs.destinationPath + @"\"; }
            if (!Directory.Exists(comandLineArgs.destinationPath)) { comandLineArgs.errorMessage = comandLineArgs.errorMessage + "Destination path don't exist!\n"; }

            int i;
            i = Array.IndexOf(args, "-ld");
            if (i != -1)
            {
                if (i + 1 < args.Length) 
                {
                    comandLineArgs.logPath = args[i + 1];
                    if (!comandLineArgs.logPath.EndsWith(@"\")) { comandLineArgs.logPath = comandLineArgs.logPath + @"\"; }
                    if (!Directory.Exists(comandLineArgs.logPath)) { comandLineArgs.errorMessage = comandLineArgs.errorMessage + "Log path don't exist!\n"; }
                }
                else
                {
                    comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-ld parameter is set but no log path was found!\n";
                }
            }


            i = Array.IndexOf(args, "-i");
            if (i != -1)
            {
                if (i + 1 < args.Length)
                {
                    try
                    {
                        comandLineArgs.syncInterval = Convert.ToInt32(args[i + 1]);
                        if (comandLineArgs.syncInterval == 0) { comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-i must be at least 1 minute!\n"; }
                    }
                    catch (OverflowException)
                    {
                        comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-i parameter is outside the range of the Int32 type!\n";
                    }
                    catch (FormatException)
                    {
                        comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-i parameter is not in a recognizable format!\n";
                    }
                }
                else
                {
                    comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-i parameter is set but no number was found!\n";
                }
            }


            i = Array.IndexOf(args, "-c");
            if (i != -1)
            {
                if (i + 1 < args.Length)
                {
                    string comparativeMethod = args[i + 1];

                    switch (comparativeMethod.ToLower())
                    {
                        case "md5":
                            comandLineArgs.comparativeMethod = 1;
                            break;

                        case "bytes":
                            comandLineArgs.comparativeMethod = 2;
                            break;

                        default:
                            comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-c parameter is set but no valid method was found!\n";
                            break;

                    }
                }
                else
                {
                    comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-c parameter is set but no valid method was found!\n";
                }
            }


            i = Array.IndexOf(args, "-v");
            if (i != -1)
            {
                if (i + 1 < args.Length)
                {
                    string verboseLogging = args[i + 1];

                    switch (verboseLogging.ToLower())
                    {
                        case "no":
                            comandLineArgs.verboseLogging = false;
                            break;

                        case "yes":
                            comandLineArgs.verboseLogging = true;
                            break;

                        default:
                            comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-v parameter is set but no valid parameter was found!\n";
                            break;

                    }
                }
                else
                {
                    comandLineArgs.errorMessage = comandLineArgs.errorMessage + "-v parameter is set but no valid parameter was found!\n";
                }
            }

            return comandLineArgs;
        }

        // main function for timer to run
        // !!! there is no solution yet for possibility when synchronization took longer than interval for repeated process !!!
        private static void RunApp(Object source, ElapsedEventArgs e)
        {
            WriteLog("Synchronization_Start", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss"));

            UpdateDirectory(ComandlineArguments);

            WriteLog("Synchronization_Finish", DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") + "\n----------------------------");

            aTimer.Interval = ComandlineArguments.syncInterval * 60000;
            aTimer.AutoReset = true;
        }


        // - check and process command line parameters
        // - if everything ok then set timer with function RunApp
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                DisplayHelp();
            }
            else
            {
                ComandlineArguments = ProcessArguments(args);

                if (ComandlineArguments.errorMessage.Equals(""))
                {
                    Console.WriteLine("\nPress any key to exit the application...\n");

                    aTimer = new System.Timers.Timer(1);
                    aTimer.Elapsed += RunApp;
                    aTimer.AutoReset = false;
                    aTimer.Enabled = true;

                    Console.ReadKey();

                    aTimer.Stop();
                    aTimer.Dispose();
                }
                else
                {
                    Console.WriteLine("\n\nSome of command line parameters was not valid. Please see error message:\n");
                    Console.WriteLine(ComandlineArguments.errorMessage);
                    DisplayHelp();
                }

            }
        }



    }
}

