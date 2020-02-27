using Microsoft.ApplicationInsights;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Postline2
{
    public class FileManager
    {
        public enum StatusCode
        {
            Error = -1,
            New = 0,
            OK = 1
        }
        private TelemetryClient TelemetryClient;
        private readonly int AllowedAttempts = Convert.ToInt32(ConfigurationManager.AppSettings["TotalAllowedAttemptsForFileProcessing"]);
        private readonly double AttemptEveryXMin = Convert.ToDouble(ConfigurationManager.AppSettings["AttemptEveryXMin"]);
        private readonly string FilePrefix = Process.GetCurrentProcess().Id.ToString();
        private readonly string SourceFolderPath = ConfigurationManager.AppSettings["SourceFolderPath"];
        private readonly string DestinationFolderPathOK = ConfigurationManager.AppSettings["OKDestinationFolderPath"];
        private readonly string DestinationFolderPathNew = ConfigurationManager.AppSettings["NewDestinationFolderPath"];
        private readonly string DestinationFolderPathError = ConfigurationManager.AppSettings["ErrorDestinationFolderPath"];

        public FileManager(TelemetryClient telemetryClient)
        {
            TelemetryClient = telemetryClient;
            CheckJammedFiles();
            //CheckFilesCounters();
            Init();
        }
        private void Init()
        {
            if (!new DirectoryInfo(DestinationFolderPathOK).Exists)
            {
                TelemetryClient.TrackEvent("DestinationFolderOK not found, creating new");
                new DirectoryInfo(DestinationFolderPathOK).Create();
            }
            if (!new DirectoryInfo(DestinationFolderPathError).Exists)
            {
                TelemetryClient.TrackEvent("DestinationFolderError not found, creating new");
                new DirectoryInfo(DestinationFolderPathError).Create();
            }
            if (!new DirectoryInfo(DestinationFolderPathNew).Exists)
            {
                TelemetryClient.TrackEvent("DestinationFolderNew not found, creating new");
                new DirectoryInfo(DestinationFolderPathNew).Create();
            }
        }
        public bool AnyFilesLeft()
        {
            return new DirectoryInfo(SourceFolderPath)
                .GetFiles()
                .Where(x => x.Name[0] == '$' 
                || x.LastWriteTime < DateTime.Now.AddMinutes(-AttemptEveryXMin))       
                .Count() != 0;
        }
        public FileInfo GetFile()
        {
            FileInfo file = new DirectoryInfo(SourceFolderPath)
                .GetFiles()
                .Where(x => 
                x.Name[0] == '$' || 
                x.LastWriteTime < DateTime.Now.AddMinutes(-AttemptEveryXMin))
                .First();
            return AddToPrefixCounter(AddProcessPrefix(file));
        }
        public FileInfo[] GetFiles()
        {
            string sourceFolderPath = @"C:\Users\Vadim Protsenko\Desktop\Core\Postline1_Data\";
            FileInfo[] files = new DirectoryInfo(SourceFolderPath)
                .GetFiles()
                .Where(x => x.Name[0] == '$' || x.LastWriteTime < DateTime.Now.AddMinutes(-AttemptEveryXMin))
                .ToArray();
            return files;
        }
        private void CheckJammedFiles() //If File is Locked for over an hour (?), remove the prefix
        {
            double expirationPeriod = Convert.ToDouble(ConfigurationManager.AppSettings["ExpirationPeriodInHours"]);
            FileInfo[] fileInfos = new DirectoryInfo(SourceFolderPath).GetFiles();
            foreach (var item in fileInfos)
            {
                if (item.LastAccessTime < DateTime.Now.AddHours(- expirationPeriod))
                {
                    RemoveProcessPrefix(item);
                }
            }
        }
        public bool IsFileCounterValid(FileInfo file)
        {
            if (!IsCounterValid(file))
            {
                TelemetryClient.TrackException(new Exception($"File {file.Name} has exceeded allowed amount of {AllowedAttempts} attempts"));
                TelemetryClient.Flush();
                //MoveError(file);
                return false;
            }
            return true;
        }
        public FileInfo MoveOK(FileInfo file)
        {
            File.Move(file.FullName, DestinationFolderPathOK + file.Name);
            return new FileInfo(DestinationFolderPathOK + file.Name);
        }
        public FileInfo MoveError(FileInfo file)
        {
            TelemetryClient.TrackEvent($"Error File: {file.Name}");
            File.Move(file.FullName, DestinationFolderPathError + file.Name);
            return new FileInfo(DestinationFolderPathError + file.Name);
        }
        public FileInfo ManageError(FileInfo file)
        {
            return RemoveProcessPrefix(file);
        }
        public FileInfo MoveNew(FileInfo file)
        {
            File.Move(file.FullName, DestinationFolderPathNew + file.Name);
            return new FileInfo(DestinationFolderPathNew + file.Name);
        }
        public FileInfo SetModifyTime(FileInfo file)
        {
            file.LastWriteTime = DateTime.Now;
            return file;
        }

        #region PrefixMethods
        //Files naming: PID $ Counter $ FileName . Extension
        //Default: $ 0 $ FileName . Extension
        private FileInfo AddProcessPrefix(FileInfo file)
        {
            FileInfo fileInfo = file;
            if(fileInfo.Name[0] != '$')
            {
                fileInfo = RemoveProcessPrefix(fileInfo);
            }
            File.Move(fileInfo.FullName, fileInfo.DirectoryName + "\\" + FilePrefix + fileInfo.Name);
            return new FileInfo(fileInfo.DirectoryName + "\\" + FilePrefix + fileInfo.Name);
        }
        private FileInfo RemoveProcessPrefix(FileInfo file)
        {
            string fileName = "$" + file.Name.Split('$')[1] + "$" + file.Name.Split('$')[2];
            string finalPath = file.DirectoryName + "\\" + fileName;
            File.Move(file.FullName, finalPath);
            return new FileInfo(finalPath);

        }
        private FileInfo AddToPrefixCounter(FileInfo file)
        {
            string[] nameParts = file.Name.Split('$');
            string fileName = String.Join('$', nameParts[0], Convert.ToInt32(nameParts[1]) + 1, nameParts[2]);
            File.Move(file.FullName, file.DirectoryName + "\\" + fileName);
            return new FileInfo(file.DirectoryName + "\\" + fileName);
        }
        private bool IsCounterValid(FileInfo file)
        {
            if (Convert.ToInt32(file.Name.Split('$')[1]) < AllowedAttempts)
                return true;
            return false;
        }
        #endregion

        #region Status Code Methods
        public int ReadForStatusCodeInnerXML(FileInfo file)
        {
            string fileContent = File.ReadAllText(file.FullName, Encoding.UTF8);
            //string fileContent = File.ReadAllText(file.DirectoryName + "\\" + FilePrefix + file.Name, Encoding.UTF8);

            if (fileContent.Contains(GetXMLStatusCodeString(StatusCode.OK)))
            {
                return (int)StatusCode.OK;
            }
            else if (fileContent.Contains(GetXMLStatusCodeString(StatusCode.New)))
            {
                return (int)StatusCode.New;
            }
            else if (fileContent.Contains(GetXMLStatusCodeString(StatusCode.Error)))
            {
                return (int)StatusCode.Error;
            }
            else
            {
                TelemetryClient.TrackException(new Exception($"Status Code Error for {file.FullName}"));
                TelemetryClient.Flush();
                throw new Exception("StatusCodeException");
                //return (int)StatusCode.Error;

            }
        }
        public int ReadForStatusCodeInnerJSON(FileInfo file)
        {
            string fileContent = File.ReadAllText(file.FullName, Encoding.UTF8);
            if (fileContent.Contains(GetJSONStatusCodeString(StatusCode.OK)))
            {
                return (int)StatusCode.OK;
            }
            else if (fileContent.Contains(GetJSONStatusCodeString(StatusCode.New)))
            {
                return (int)StatusCode.New;
            }
            else
            {
                TelemetryClient.TrackException(new Exception($"Status Code Error for {file.FullName}"));
                TelemetryClient.Flush();
                return (int)StatusCode.Error;

            }
        }
        public int ReadForStatusCodeOuter(FileInfo file)
        {
            if (file.Name.Contains(StatusCode.OK.ToString()))
            {
                return (int)StatusCode.OK;
            }
            else if (file.Name.Contains(StatusCode.New.ToString()))
            {
                return (int)StatusCode.New;
            }
            else
            {
                TelemetryClient.TrackException(new Exception($"Status Code Error for {file.FullName}"));
                TelemetryClient.Flush();
                return (int)StatusCode.Error;

            }
        }
        private string GetXMLStatusCodeString(StatusCode code)
        {
            return "<StatusCode>" + (int)code + "</StatusCode>";
        }
        private string GetJSONStatusCodeString(StatusCode code)
        {
            return $"\"StatusCode\": \"" + (int)code + $"\"";
        }
        #endregion

    }
}
