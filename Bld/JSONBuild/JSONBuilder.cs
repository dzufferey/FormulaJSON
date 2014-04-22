using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace JSONBuild
{
    class JSONBuilder
    {
        private const string MsBuildCommand = "\"{0}\" /p:Configuration={1} /p:Platform={2}";
        private const string ConfigDebug = "Debug";
        private const string ConfigRelease = "Release";
        //private const string PlatformX86 = "x86";
        //private const string PlatformX64 = "x64";
        private const string PlatformAny = "AnyCPU";

        /// <summary>
        /// Project is described by:
        /// (1) true if can only be built with 32-bit version of MsBuild (e.g. VS extensions)
        /// (2) the relative location of the project file
        /// (3) the platform on which it should be built
        /// </summary>
        private static readonly Tuple<bool, string, string>[] Projects = new Tuple<bool, string, string>[]
        {
            new Tuple<bool, string, string>(false, "..\\..\\..\\..\\JSONParser\\JSONParser.csproj", PlatformAny),
        };
        
        private static readonly Tuple<string, string>[] DebugMoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Core.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\Core.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Microsoft.Z3.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\Microsoft.Z3.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\libz3.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\libz3.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Formula.exe", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\Formula.exe"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\JSON.4ml", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\PData.4ml"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\JSONParser\\bin\\Debug\\JSONParser.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Debug\\JSONParser.dll")
        };

        private static readonly Tuple<string, string>[] ReleaseMoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Core.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\Core.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Microsoft.Z3.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\Microsoft.Z3.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\libz3.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\libz3.dll"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\Ext\\Formula\\Formula.exe", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\Formula.exe"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\JSON.4ml", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\PData.4ml"),
            new Tuple<string, string>(
                "..\\..\\..\\..\\JSONParser\\bin\\Release\\JSONParser.dll", 
                "..\\..\\..\\Drops\\FormulaJSON_Release\\JSONParser.dll")
        };

        public static bool Build(bool isBldDebug)
        {
            var result = true;
            FileInfo msbuild, msbuild32 = null;
            result = SourceDownloader.GetMsbuild(out msbuild) && 
                     SourceDownloader.GetMsbuild(out msbuild32, true) &&
                     result;
            if (!result)
            {
                Program.WriteError("Could not build FormulaJSON, unable to find msbuild");
                return false;
            }

            var config = isBldDebug ? ConfigDebug : ConfigRelease;
            foreach (var proj in Projects)
            {
                Program.WriteInfo("Building {0}: Config = {1}, Platform = {2}", proj.Item2, config, proj.Item3);
                result = BuildCSProj(proj.Item1 ? msbuild32 : msbuild, proj.Item2, config, proj.Item3) && result;
            }

            if (!result)
            {
                return false;
            }

            result = DoMove(isBldDebug ? DebugMoveMap : ReleaseMoveMap) && result;

            return result;
        }

        private static bool DoMove(Tuple<string, string>[] moveMap)
        {
            bool result = true;
            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                foreach (var t in moveMap)
                {
                    var inFile = new FileInfo(Path.Combine(runningLoc.Directory.FullName, t.Item1));
                    if (!inFile.Exists)
                    {
                        result = false;
                        Program.WriteError("Could not find output file {0}", inFile.FullName);
                        continue;
                    }

                    var outFile = new FileInfo(Path.Combine(runningLoc.Directory.FullName, t.Item2));
                    if (!outFile.Directory.Exists)
                    {
                        outFile.Directory.Create();
                    }

                    inFile.CopyTo(outFile.FullName, true);
                    Program.WriteInfo("Moved output {0} --> {1}", inFile.FullName, outFile.FullName);
                }

                return result;
            }
            catch (Exception e)
            {
                Program.WriteError("Unable to move output files - {0}", e.Message);
                return false;
            }
        }

        private static bool BuildCSProj(FileInfo msbuild, string projFileName, string config, string platform)
        {
            try
            {
                FileInfo projFile;
                if (!SourceDownloader.GetBuildRelFile(projFileName, out projFile) || !projFile.Exists)
                {
                    Program.WriteError("Could not find project file {0}", projFileName);
                }

                var psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.WorkingDirectory = projFile.Directory.FullName;
                psi.FileName = msbuild.FullName;
                psi.Arguments = string.Format(MsBuildCommand, projFile.Name, config, platform);
                psi.CreateNoWindow = true;

                var process = new Process();
                process.StartInfo = psi;
                process.OutputDataReceived += OutputReceived;
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();

                Program.WriteInfo("EXIT: {0}", process.ExitCode);
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Program.WriteError("Failed to build project {0} - {1}", projFileName, e.Message);
                return false;
            }
        }

        private static void OutputReceived(
            object sender,
            DataReceivedEventArgs e)
        {
            Console.WriteLine("OUT: {0}", e.Data);
        }
    }
}
