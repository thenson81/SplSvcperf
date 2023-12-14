﻿using System;
using System.IO;
using System.Reflection;
using EtlViewer;

namespace Utilities
{
    /// <summary>
    /// SupportFiles is a class that manages the unpacking of DLLs and other reasources.  
    /// This allows you to make your EXE the 'only' file in the distribution, and all other
    /// files are unpacked from that EXE.   
    /// 
    /// To add a file to the EXE as a resource you need to add the following lines to the .csproj
    /// In the example below we added the TraceEvent.dll file (relative to the project directory).
    /// LogicalName must start with a .\ and is the relative path from the SupportFiles directory
    /// where the file will be placed.  Adding the Link tag makes it show up in a pretty way in
    /// solution explorer.  
    /// 
    /// <ItemGroup>
    ///  <EmbeddedResource Include="..\TraceEvent\$(OutDir)TraceEvent.dll">
    ///   <Type>Non-Resx</Type>
    ///   <WithCulture>false</WithCulture>
    ///   <LogicalName>.\TraceEvent.dll</LogicalName>
    ///   <Link>SupportDlls\TraceEvent.dll</Link>
    ///  </EmbeddedResource>
    /// </ItemGroup>
    /// 
    /// </summary>
    static class SupportFiles
    {
        /// <summary>
        /// Unpacks any resource that begings with a .\ (so it looks like a relative path name)
        /// Such resources are unpacked into their relative position in SupportFileDir. 
        /// 'force' will force an update even if the files were unpacked already (usually not needed)
        /// The function returns true if files were unpacked.  
        /// </summary>
        public static bool UnpackResourcesIfNeeded(bool force = false)
        {
            var filesExist = Directory.Exists(SupportFileDir);
            if (filesExist)
            {
                if (force)
                {
                    filesExist = false;
                    DirectoryUtilities.Clean(SupportFileDir);
                    UnpackResources();
                }
            }
            else
                UnpackResources();

            // Do we need to cleanup old files?
            if (filesExist && File.Exists(Path.Combine(SupportFileDirBase, "CleanupNeeded")))
                Cleanup();

            // Register a Assembly resolve event handler so that we find our support dlls in the support dir.
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                var simpleName = args.Name;
                var commaIdx = simpleName.IndexOf(',');
                if (0 <= commaIdx)
                    simpleName = simpleName.Substring(0, commaIdx);
                string fileName = Path.Combine(SupportFileDir, simpleName + ".dll");
                if (File.Exists(fileName))
                    return System.Reflection.Assembly.LoadFrom(fileName);

                fileName = Path.Combine(SupportFileDir, simpleName + ".exe");
                if (File.Exists(fileName))
                    return System.Reflection.Assembly.LoadFrom(fileName);

                // Also look in processor specific location
                fileName = Path.Combine(SupportFileDir, ProcessArch, simpleName + ".dll");
                if (File.Exists(fileName))
                    return System.Reflection.Assembly.LoadFrom(fileName);

                // And look for an exe (we need this for HeapDump.exe)
                fileName = Path.Combine(SupportFileDir, ProcessArch, simpleName + ".exe");
                if (File.Exists(fileName))
                    return System.Reflection.Assembly.LoadFrom(fileName);
                return null;
            };

            return !filesExist;
        }

        /// <summary>
        /// SupportFileDir is a directory that is reserved for CURRENT VERSION of the software (if a later version is installed)
        /// It gets its own directory).   This is the directory where files in the EXE get unpacked to.  
        /// </summary>
        public static string SupportFileDir
        {
            get
            {
                if (s_supportFileDir == null)
                {
                    var exeLastWriteTime = File.GetLastWriteTime(ExePath);
                    var version = exeLastWriteTime.ToString("VER.yyyy'-'MM'-'dd'.'HH'.'mm'.'ss.fff");
                    s_supportFileDir = Path.Combine(SupportFileDirBase, version);
                }
                return s_supportFileDir;
            }
            set { s_supportFileDir = value; }

        }

        public static string AppVersion
        {
            get
            {
                var exeLastWriteTime = File.GetLastWriteTime(ExePath);
                return exeLastWriteTime.ToString("VER.yyyy'-'MM'-'dd'.'HH'.'mm'.'ss.fff");
            }
        }
        /// <summary>
        /// You must have write access to this directory.  It does not need to exist, but 
        /// if not, users have to have permission to create it.   This directory should only
        /// be used for this app only (not shared with other things).    By default we choose
        /// %APPDATA%\APPNAME where APPNAME is the name of the application (EXE file name 
        /// without the extension). 
        /// </summary>
        public static string SupportFileDirBase
        {
            get
            {
                if (s_supportFileDirBase == null)
                {
                    string appName = Path.GetFileNameWithoutExtension(ExePath);
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    s_supportFileDirBase = Path.Combine(appData, appName);
                }
                return s_supportFileDirBase;
            }
            set { s_supportFileDirBase = value; }
        }
        /// <summary>
        /// The path to the executable.   You should not be writing here! that is what SupportFileDir is for.  
        /// </summary>
        public static string ExePath
        {
            get
            {
                if (s_exePath == null)
                {
                    var exeAssembly = Assembly.GetEntryAssembly();
                    s_exePath = exeAssembly.ManifestModule.FullyQualifiedName;
                }
                return s_exePath;
            }
        }
        /// <summary>
        /// Get the name of the architecture of the current process
        /// </summary>
        public static string ProcessArch
        {
            get
            {
                if (s_ProcessArch == null)
                {
                    s_ProcessArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                    // This should not be needed, but when I run PerfView under VS from an extension on an X64 machine
                    // the environment variable is wrong.  
                    if (s_ProcessArch == "AMD64" && System.Runtime.InteropServices.Marshal.SizeOf(typeof(IntPtr)) == 4)
                        s_ProcessArch = "x86";
                }
                return s_ProcessArch;
            }
        }

        /// <summary>
        /// If you need to load an unmanaged DLL that is part of your distribution
        /// This routine will do the load library using the correct architecture
        /// </summary>
        /// <param name="relativePath"></param>
        public static void LoadNative(string relativePath)
        {
            var archPath = Path.Combine(ProcessArch, relativePath);
            var fullPath = Path.Combine(SupportFileDir, archPath);
            var ret = LoadLibrary(fullPath);
            if (ret == IntPtr.Zero)
            {
                if (!File.Exists(fullPath))
                {
                    if (ProcessArch != "x86")
                    {
                        var x86FullPath = Path.Combine(SupportFileDir, Path.Combine("x86", relativePath));
                        if (File.Exists(x86FullPath))
                            throw new ApplicationException("This operation is not supported for the " + ProcessArch + " architecture.");
                    }
                    throw new ApplicationException("Could not find Dll " + archPath + " in distrubution.  Application Error.");
                }
            }
        }

        #region private
        private static void UnpackResources()
        {
            // We don't unpack into the final directory so we can be transactional (all or nothing).  
            string prepDir = SupportFileDir + ".new";
            Directory.CreateDirectory(prepDir);

            // Unpack the files. 
            var resourceAssembly = GetResourcesAssembly();
            var archPrefix = @".\" + ProcessArch;
            foreach (var resourceName in resourceAssembly.GetManifestResourceNames())
            {
                if (resourceName.StartsWith(@".\"))
                {
                    // Unpack everything, inefficient, but insures ldr64 works.  
                    string targetPath = Path.Combine(prepDir, resourceName);
                    if (!ResourceUtilities.UnpackResourceAsFile(resourceName, targetPath, resourceAssembly))
                        throw new ApplicationException("Could not unpack support file " + resourceName);
                }
            }

            // Commit the unpack, we try several times since antiviruses often lock the directory
            for (int retries = 0; ; retries++)
            {
                try
                {
                    Directory.Move(prepDir, SupportFileDir);
                    break;
                }
                catch (Exception)
                {
                    if (retries > 5)
                        throw;
                }
                System.Threading.Thread.Sleep(100);
            }

            // See if we need to clean up old versions.  
            Cleanup();
        }

        private static Assembly GetResourcesAssembly()
        {
            //var resourceAssembly = System.Reflection.Assembly.GetEntryAssembly();
            //if (resourceAssembly == null)
            //    resourceAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            return resourceAssembly;
        }

        static void Cleanup()
        {
            string cleanupMarkerFile = Path.Combine(SupportFileDirBase, "CleanupNeeded");
            var dirs = Directory.GetDirectories(SupportFileDirBase, "VER.*");
            if (dirs.Length > 1)
            {
                // We will assume we should come and check again on our next launch.  
                File.WriteAllText(cleanupMarkerFile, "");
                foreach (string dir in Directory.GetDirectories(s_supportFileDirBase))
                {
                    // Don't clean up myself
                    if (string.Compare(dir, s_supportFileDir, StringComparison.OrdinalIgnoreCase) == 0)
                        continue;

                    // We first try to move the directory and only delete it if that succeeds.  
                    // That way directories that are in use don't get cleaned up.    
                    var deletingName = dir + ".deleting";
                    try
                    {
                        Directory.Move(dir, deletingName);
                        DirectoryUtilities.Clean(deletingName);
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                // No cleanup needed, mark that fact
                FileUtilities.ForceDelete(cleanupMarkerFile);
            }
        }

        /// <summary>
        /// This is a convinience function.  If you unpack native dlls, you may want to simply LoadLibary them
        /// so that they are guarenteed to be found when needed.  
        /// </summary>
        [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);


        private static string s_ProcessArch;


        private static string s_supportFileDir;
        private static string s_supportFileDirBase;
        public static string s_exePath;

        #endregion
    }
}
