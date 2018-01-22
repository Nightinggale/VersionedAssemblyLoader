using System.Reflection;
using Verse;
using System.IO;
using System.Diagnostics;
using System;

//using System;
using System.Collections.Generic;
using System.Collections;

// the idea with this file is to loop all loaded mods and locate and load the newest ModCheck.dll
// the issue is that if the DLL is loaded multiple times, the first load takes priority, not the newest
// even worse, if more methods/classes are added, the game could end up with a mixed version, which has undefined behavior
// using this file, only the newest is loaded and only once and with just one loaded, it's obviously the first

namespace VersionedAssemblyLoader
{
    public sealed class VersionedAssemblyLoader : Mod
    {
        
        private class DLLInfo
        {
            public string name = "";
            public string path = "";
            public string DLLName = "";
            public Version version = new Version(0,0);

            public DLLInfo(FileInfo info)
            {
                this.name = info.Name;
                this.path = info.FullName;
                this.DLLName = info.Name;

                FileVersionInfo myFileVersionInfo = FileVersionInfo.GetVersionInfo(path);

                this.version = new Version(myFileVersionInfo.FileVersion);

                // remove file extension from name
                this.name = this.name.Remove(this.name.Length - 4);

                // remove leading "garbage" from name
                while (this.name.Length > 0)
                {
                    char chr = this.name[0];
                    if (chr >= 'a' && chr <= 'z')
                    {
                        break;
                    }
                    if (chr >= 'A' && chr <= 'Z')
                    {
                        break;
                    }
                    this.name = this.name.Substring(1);
                }
            }

            public void update(FileInfo info)
            {
                FileVersionInfo myFileVersionInfo = FileVersionInfo.GetVersionInfo(info.FullName);

                Version NewVersion = new Version(myFileVersionInfo.FileVersion);

                if (this.version.CompareTo(NewVersion) < 0)
                {
                    this.version = NewVersion;
                    this.path = info.FullName;
                }
            }
        }
        

        public VersionedAssemblyLoader(ModContentPack content) : base(content)
        {
            List<string> dllFileNames = new List<string>();
            //List<FileInfo> infos = new List<FileInfo>();

            Hashtable dllFiles = new Hashtable();

            // build list of all dll files in VersionedAssemblies in all mods
            foreach (ModContentPack Pack in LoadedModManager.RunningModsListForReading)
            {
                // mostly copied from Verse.ModAssemblyHandler.ReloadAll()
                string path = Path.Combine(Pack.RootDir, "VersionedAssemblies");
                string path2 = Path.Combine(GenFilePaths.CoreModsFolderPath, path);
                DirectoryInfo directoryInfo = new DirectoryInfo(path2);
                if (!directoryInfo.Exists)
                {
                    continue;
                }
                FileInfo[] files = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo fileInfo = files[i];
                    if (!(fileInfo.Extension.ToLower() != ".dll"))
                    {
                        string name = fileInfo.Name;
                        if (name == "UnityEngine.dll")
                        {
                            // Ignore unity. It shouldn't be there, but if it's there anyway, do not try to load it
                            continue;
                        }
                        if (dllFiles.ContainsKey(name))
                        {
                            ((DLLInfo)dllFiles[name]).update(fileInfo);
                        }
                        else
                        {
                            dllFiles[name] = new DLLInfo(fileInfo);
                            dllFileNames.Add(name);
                        }
                    }
                }
            }

            // load DLL files alphabetically
            dllFileNames.Sort();
            foreach (string dll in dllFileNames)
            {
                DLLInfo info = dllFiles[dll] as DLLInfo;

                byte[] rawAssembly = File.ReadAllBytes(info.path);
                Assembly assembly = AppDomain.CurrentDomain.Load(rawAssembly);

                var asm = assembly.CreateInstance(dll + ".VersionedAssemblyInit");

                // DLL loaded, now try to call the init method
                AssemblyTitleAttribute[] attributes = assembly.GetCustomAttributes(typeof(AssemblyTitleAttribute), false) as AssemblyTitleAttribute[];
                try
                {
                    string title = attributes[0].Title;
                    assembly.CreateInstance(title + ".VersionedAssemblyInit");
                }
                catch
                {
                }
            }

            // look for incompatible dll files in assemblies
            foreach (ModContentPack Pack in LoadedModManager.RunningModsListForReading)
            {
                string path = Path.Combine(Pack.RootDir, "VersionedAssemblies");
                string path2 = Path.Combine(GenFilePaths.CoreModsFolderPath, path);
                DirectoryInfo directoryInfo = new DirectoryInfo(path2);
                if (!directoryInfo.Exists)
                {
                    continue;
                }
                FileInfo[] files = directoryInfo.GetFiles("*.*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        string Name = files[i].Name;
                        if (dllFileNames.Contains(Name))
                        {
                            FileVersionInfo myFileVersionInfo = FileVersionInfo.GetVersionInfo(files[i].FullName);
                            Version version = new Version(myFileVersionInfo.FileVersion);

                            Version a = (dllFiles[Name] as DLLInfo).version;
                            int comparision = version.CompareTo(a);

                            if (comparision < 0)
                            {
                                Log.Error("[" + Pack.Name + "] contains outdated " + Name + " which conflicts with the version loaded in VersionedAssemblies");
                            }
                            else if (comparision == 0)
                            {
                                if (Prefs.LogVerbose)
                                {
                                    Log.Warning("[" + Pack.Name + "] contains " + Name + " which shouldn't be in assemblies when it's also loaded in VersionedAssemblies");
                                }
                            }
                            else if (comparision > 0)
                            {
                                Log.Warning("[" + Pack.Name + "] contains a newer " + Name + " than the newest in VersionedAssemblies");
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
