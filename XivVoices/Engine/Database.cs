﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Directory = System.IO.Directory;
using File = System.IO.File;
using WebSocketSharp;
using Dalamud.Plugin;
using Dalamud.Logging;
using System.Threading.Tasks;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace XivVoices.Engine
{
    public class Database
    {

        #region Private Parameters
        private bool notificationBusy = false;
        #endregion

        #region Public Parameters
        public string RootPath { get; set; } = "C:/XIV_Voices";
        public string DirectoryPath { get; set; } = "C:/XIV_Voices";
        public string VoiceFilesPath { get { return Path.Combine(DirectoryPath, "Data"); } }
        public string ToolsPath { get { return Path.Combine(DirectoryPath, "Tools");  } }
        public string Firstname { get; } = "_FIRSTNAME_";
        public string Lastname { get; } = "_LASTNAME_";
        public bool GameUpdated { get; set; } = false;
        public Dictionary<string, XivNPC> Npcs { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public Dictionary<string, string> VoiceNames { get; set; }
        public Dictionary<string, string> Lexicon { get; set; }
        public Dictionary<string, string> Nameless { get; set; }
        public Dictionary<string, string> Retainers { get; set; }
        public List<string> Ignored { get; set; }
        public Framework Framework { get; set; }
        #endregion

        private DalamudPluginInterface _pluginInterface;
        public Plugin Plugin;
        public IClientState ClientState;

        #region Unity Methods
        public Database(DalamudPluginInterface pluginInterface, Plugin plugin, IClientState clientState)
        {
            this._pluginInterface = pluginInterface;
            this.Plugin = plugin;
            this.ClientState = clientState;
            Framework = new Framework();

            string pathWith_Data = _pluginInterface.AssemblyLocation.DirectoryName;
            RootPath = Path.GetDirectoryName(pathWith_Data);
            
            string directory = this.Plugin.Config.WorkingDirectory;
            DirectoryPath = directory;

            this.Plugin.Config.Initialized = false;
            bool dataAndToolsExist = true;

            // Check for Data folder
            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
                dataAndToolsExist = false;
            }

            // Check for Data folder
            if (!Directory.Exists(VoiceFilesPath))
            {
                Directory.CreateDirectory(VoiceFilesPath);
                dataAndToolsExist = false;
            }

            // Check for Tools folder
            if (!Directory.Exists(ToolsPath))
            {
                Directory.CreateDirectory(ToolsPath);
                dataAndToolsExist = false;
            }
            else
            {
                
                bool ffmpegExists = File.Exists(Path.Combine(ToolsPath, "ffmpeg.exe"));
                bool ffprobeExists = File.Exists(Path.Combine(ToolsPath, "ffprobe.exe"));

                if (!ffmpegExists || !ffprobeExists)
                    dataAndToolsExist = false;
            }

            if(dataAndToolsExist)
                this.Plugin.Config.Initialized = true;

            

            PluginLog.Information("Working directory is: " + DirectoryPath);

            // Search for any .wav files within directoryPath and all its subdirectories
            var wavFiles = Directory.GetFiles(VoiceFilesPath, "*.wav", SearchOption.AllDirectories);
            if (wavFiles.Length > 0)
            {
                PluginLog.LogError("You have " + wavFiles.Length + " waves");
                foreach (var wavFile in wavFiles)
                    PluginLog.LogError(wavFile);
            }

            Task.Run(async () => await LoadDatabaseAsync());
            Plugin.Chat.Print("Database: I am awake");
        }

        public void UpdateDirectory()
        {
            DirectoryPath = this.Plugin.Config.WorkingDirectory;

            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }

            if (!Directory.Exists(VoiceFilesPath))
            {
                Directory.CreateDirectory(VoiceFilesPath);
            }

            if (!Directory.Exists(ToolsPath))
            {
                Directory.CreateDirectory(ToolsPath);
            }
        }

        public void Dispose()
        {

        }
        #endregion


        #region Loaders
        public async Task LoadDatabaseAsync()
        {
            try
            {
                // Assume loadingScreen and other UI components are handled differently
                Plugin.Chat.Print("Loading Data...");
                await LoadDataAsync();
                await LoadLexiconsAsync();

                Plugin.Chat.Print("Loading Nameless Data...");
                await LoadNamelessAsync();

                Plugin.Chat.Print("Loading Retainers Data...");
                await LoadRetainersAsync();

                Plugin.Chat.Print("Loading Ignored Data...");
                await LoadIgnoredAsync();

                Plugin.Chat.Print("Loading NPC Data...");
                await LoadNPCsAsync();

                Plugin.Chat.Print("Loading Voice Names...");
                //if (XivVoices.Instance.ArcFramework != null)
                //{
                //    XivVoices.Instance.ArcFramework.LoadDatabase(); // Assuming this is an async method, if not, wrap in Task.Run
                //}

                await LoadVoiceNamesAsync();

                Plugin.Chat.Print("Done.");
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Error during database load.");
            }
        }

        private async Task LoadDataAsync()
        {
            string filePath = DirectoryPath + "/data.json";
            Data = ReadFile(filePath);
            if (Data == null)
            {
                Data = new Dictionary<string, string>();
                Data["voices"] = "0";
                Data["npcs"] = "0";
                Data["actors"] = "0";
                await WriteJSON(DirectoryPath + "/Data.json", Data); 
            }
            if (GameUpdated)
                await ReloadAndUpdateData();
        }

        public async Task LoadLexiconsAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.lexicon.json";
            Lexicon = ReadResourceFile(resourceName);
            if (Lexicon == null)
            {
                PluginLog.LogError("Failed to load Lexicon.json from Resources.");
                Lexicon = new Dictionary<string, string>();
            }
        }

        public async Task LoadNamelessAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.nameless.json";
            Nameless = ReadResourceFile(resourceName);
            if (Nameless == null)
            {
                Nameless = new Dictionary<string, string>();
            }
        }

        public async Task LoadRetainersAsync()
        {
            string resourceName = "XivVoices.Data.retainers.json";
            Retainers = ReadResourceFile(resourceName);
            if (Retainers == null)
            {
                PluginLog.LogError("Failed to load Retainers.json from Resources.");
                Retainers = new Dictionary<string, string>();
            }
        }

        public async Task LoadIgnoredAsync(bool announce = false)
        {
            string resourceName = "XivVoices.Data.ignored.json";
            Ignored = ReadResourceList(resourceName);
            if (Ignored == null)
            {
                Ignored = new List<string>();
            }
        }

        public async Task LoadNPCsAsync()
        {
            string resourceName = "XivVoices.Data.npcData.json";
            string filePath = DirectoryPath + "/npcData.json";
            Npcs = ReadResourceNPCs(resourceName);
            if (Npcs == null)
            {
                Npcs = new Dictionary<string, XivNPC>();
                PluginLog.LogError("Something is wrong with the NPC database");
            }
        }

        public async Task LoadVoiceNamesAsync()
        {
            string resourceName = "XivVoices.Data.voiceNames.json";
            string json = ReadResourceEmbedded(resourceName);
            if (json == null)
            {
                VoiceNames = new Dictionary<string, string>();
                PluginLog.LogError("Failed to load voiceNames.json from embedded resources.");
                return;
            }

            List<VoiceMapping> voiceMappings = JsonConvert.DeserializeObject<List<VoiceMapping>>(json);
            if (voiceMappings == null)
            {
                VoiceNames = new Dictionary<string, string>();
                PluginLog.LogError("Failed to deserialize voiceNames.json.");
                return;

            }

            VoiceNames = new Dictionary<string, string>();

            foreach (VoiceMapping mapping in voiceMappings)
            {
                foreach (string speaker in mapping.speakers)
                {
                    VoiceNames[speaker] = mapping.voiceName;
                }
            }
        }

        public void OnClick_Reload()
        {
            Task.Run(async () => await ReloadAsync());
        }

        private async Task ReloadAsync()
        {
            await LoadLexiconsAsync(true);
            await LoadNamelessAsync(true);
            await LoadIgnoredAsync(true);
            await LoadNPCsAsync();
            //if (XivVoices.Instance.ArcFramework) XivVoices.Instance.ArcFramework.LoadDatabase();
            await LoadVoiceNamesAsync();
        }
        #endregion


        #region Readers

        public Dictionary<string, string> ReadResourceFile(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
            }
        }

        public List<string> ReadResourceList(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<List<string>>(jsonContent);
            }
        }

        public static string ReadResourceEmbedded(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    PluginLog.LogError($"Failed to load resource: {resourceName}");
                    return null;
                }
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        public Dictionary<string, string> ReadFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                Dictionary<string, string> jsonDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);
                return jsonDictionary;
            }
            else
            {
                return null;
            }
        }

        public Dictionary<string, XivNPC> ReadResourceNPCs(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                PluginLog.LogError($"Failed to find embedded resource: {resourceName}");
                return null;
            }

            using (var reader = new StreamReader(resourceStream))
            {
                string jsonContent = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<Dictionary<string, XivNPC>>(jsonContent);
            }
        }


        Dictionary<string, XivNPC> ReadNPCs(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                Dictionary<string, XivNPC> jsonDictionary = JsonConvert.DeserializeObject<Dictionary<string, XivNPC>>(jsonContent);
                PluginLog.Information(jsonDictionary.Count + " NPCs have been loaded.");
                return jsonDictionary;
            }
            else
            {
                return null;
            }
        }
        #endregion


        #region Writers
        public async Task WriteJSON<T>(string filePath, T dataToSave)
        {
            string jsonContent = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);

            // Asynchronously write text to the file
            await File.WriteAllTextAsync(filePath, jsonContent);

            PluginLog.Information("JSON file written successfully to path: " + filePath);
        }

        /*
        public void WriteVoiceData(string voiceName, string speaker, string sentence, AudioClip clip)
        {
           Task.Run(async () => await WriteVoiceDataCoroutine(voiceName, speaker, sentence, clip));
        }

        bool writeVoiceDataBusy = false;
        public async Task WriteVoiceDataCoroutine(string voiceName, string speaker, string sentence, AudioClip clip)
        {
            while (writeVoiceDataBusy)
                
            writeVoiceDataBusy = true;

            string originalSpeaker = speaker;
            speaker = Regex.Replace(speaker, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");
            // Create a Path 
            string cleanedSentence = RemoveSymbolsAndLowercase(sentence);
            if (cleanedSentence.IsNullOrEmpty())
            {
                writeVoiceDataBusy = false;
                return;
            }
            string actorDirectory = VoiceFilesPath + "/" + voiceName;
            string speakerDirectory = actorDirectory + "/" + speaker;

            string filePath = speakerDirectory + "/" + cleanedSentence;
            int missingFromDirectoryPath = 0;
            if (directoryPath.Length < 13)
                missingFromDirectoryPath = 13 - directoryPath.Length;
            int maxLength = 200 - ((directoryPath + "/" + voiceName + "/" + speaker).Length);
            maxLength -= missingFromDirectoryPath;
            if (cleanedSentence.Length > maxLength)
                cleanedSentence = cleanedSentence.Substring(0, maxLength);
            filePath = speakerDirectory + "/" + cleanedSentence;

            // Check if file exists
            bool fileExistedBefore;
            if (File.Exists(filePath + ".ogg"))
                fileExistedBefore = true;
            else
                fileExistedBefore = false;

            // Create the directory if it doesn't exist
            if (!Directory.Exists(actorDirectory))
            {
                Directory.CreateDirectory(actorDirectory);
                Data["actors"] = (int.Parse(Data["actors"]) + 1).ToString();
            }

            if (!Directory.Exists(speakerDirectory))
            {
                Directory.CreateDirectory(speakerDirectory);
                Data["npcs"] = (int.Parse(Data["npcs"]) + 1).ToString();
            }

            // Save clip as WAV in filePath
            bool success = WavUtil.Save(filePath + ".wav", clip);

            if (success)
            {
                PluginLog.Information("WAV file written successfully to path: " + filePath + ".wav");
                if (!fileExistedBefore)
                {
                    Data["voices"] = (int.Parse(Data["voices"]) + 1).ToString();
                }

                // Save JSON Data for this WAV
                Dictionary<string, string> wavdata = new Dictionary<string, string>();
                wavdata["speaker"] = originalSpeaker;
                wavdata["sentence"] = sentence;
                wavdata["lastSave"] = DateTime.UtcNow.ToString("o");
                await WriteJSON(filePath + ".json", wavdata);
            }
            else
            {
                PluginLog.LogError("Failed to write WAV file to path: " + filePath + ".wav");
                Notification("Error: Failed to write WAV file to path: " + filePath + ".wav");
            }

            await WriteJSON(directoryPath + "/Data.json", Data);
            //XivVoices.Instance.UpdateText();
            writeVoiceDataBusy = false;
        }
        */
        #endregion


        #region OnClick Methods
        public void OnClick_OpenFile(string fileName)
        {
            string filePath = "file:" + DirectoryPath + "/" + fileName;
            Process.Start(filePath);
        }

        public void OnClick_ReloadAndUpdateData()
        {
            Task.Run(async () => await ReloadAndUpdateData());
        }

        async Task ReloadAndUpdateData()
        {
            int _actors = Directory.GetDirectories(VoiceFilesPath).Count();
            int _voices = 0;
            int _npcs = 0;
            foreach (var directory in Directory.GetDirectories(VoiceFilesPath))
            {
                foreach (var voiceDirectory in Directory.GetDirectories(directory))
                {
                    _npcs++;
                    _voices += Directory.GetFiles(voiceDirectory, "*.ogg").Length;
                }
            }
            Data["voices"] = _voices.ToString();
            Data["npcs"] = _npcs.ToString();
            Data["actors"] = _actors.ToString();
            await WriteJSON(DirectoryPath + "/Data.json", Data);
            //XivVoices.Instance.UpdateText();
        }

        public void SetIgnored(string speaker)
        {
            Task.Run(async () => await SetIgnoredCoroutine(speaker));
        }

        async Task SetIgnoredCoroutine(string speaker)
        {
            Ignored.Add(speaker);
        }
        #endregion


        #region Utility
        public string VoiceDataExists(string voiceName, string speaker, string sentence)
        {
            speaker = Regex.Replace(speaker, @"[^a-zA-Z0-9 _-]", "").Replace(" ", "_").Replace("-", "_");

            // Create a Path
            string cleanedSentence = Regex.Replace(sentence, "<[^<]*>", "");
            cleanedSentence = RemoveSymbolsAndLowercase(cleanedSentence);
            string actorDirectory = VoiceFilesPath + "/" + voiceName;
            string speakerDirectory = actorDirectory + "/" + speaker;

            string filePath = speakerDirectory + "/" + cleanedSentence;
            int missingFromDirectoryPath = 0;
            if (DirectoryPath.Length < 13)
                missingFromDirectoryPath = 13 - DirectoryPath.Length;
            int maxLength = 200 - ((DirectoryPath + "/" + voiceName + "/" + speaker).Length);
            maxLength -= missingFromDirectoryPath;
            if (cleanedSentence.Length > maxLength)
                cleanedSentence = cleanedSentence.Substring(0, maxLength);
            filePath = speakerDirectory + "/" + cleanedSentence;

            // Get Wav from filePath if it exists and return it as AudioClip, if not, return Null
            PluginLog.Information($"looking for path [{filePath + ".ogg"}]");
            if (File.Exists(filePath + ".ogg"))
            {
                string jsonContent = File.ReadAllText(filePath + ".json");
                var json = JsonConvert.DeserializeObject<DialogueData>(jsonContent);

                //PluginLog.Information(json.sentence);
                var sentence_no_spaces = Regex.Replace(sentence, @"\s+", "").Trim();
                var pattern = "\\b" + "Arc" + "\\b";
                var json_no_space = Regex.Replace(json.sentence, pattern, "_NAME_");
                //PluginLog.Information(json_no_space);
                json_no_space = Regex.Replace(json_no_space, @"\s+", "").Trim();
                if (json_no_space.StartsWith("..."))
                    json_no_space = json_no_space[3..];
                //PluginLog.Information(json_no_space);

                PluginLog.Information("Sentence from XIVV:" + sentence_no_spaces);
                PluginLog.Information("Sentence from JSON:" + json_no_space);
                if (sentence_no_spaces != json_no_space && !sentence.Contains("<"))
                    return "report";
                else
                    return filePath + ".ogg";
            }
            else if (File.Exists(filePath + ".wav"))
                return filePath + ".wav";
            else
                return null;
        }
        [Serializable]
        public class DialogueData
        {
            public string speaker;
            public string sentence;
            public string lastSave;
        }

        string RemoveSymbolsAndLowercase(string input)
        {
            // Replace player name with Adventurer before loading or saving
            string pattern = "\\b" + "_NAME_" + "\\b";
            string result = Regex.Replace(input, pattern, "Adventurer");

            StringBuilder stringBuilder = new StringBuilder();
            foreach (char c in result)
            {
                if (char.IsLetter(c))
                {
                    stringBuilder.Append(char.ToLower(c));
                }
            }
            return stringBuilder.ToString();
        }

        public XivNPC GetNPC(string npcName, string npcId, TTSData ttsData, ref bool fetchedByID)
        {
            PluginLog.Information("GetNPC: " + npcName + " - " + npcId);

            if (npcName == "Bubble" && ttsData != null)
            {
                XivNPC npc = new XivNPC();
                npc.Gender = ttsData.Gender;
                npc.Race = ttsData.Race;
                npc.Clan = ttsData.Tribe;
                npc.BodyType = ttsData.Body;
                npc.EyeShape = ttsData.Eyes;
                return npc;
            }
            if (!npcId.IsNullOrEmpty() && Npcs.ContainsKey(npcId))
            {
                fetchedByID = true;
                return Npcs[npcId];
            }
            else if (Npcs.ContainsKey(npcName))
                return Npcs[npcName];
            else
                return null;
        }

        public XivMessage GetNameless(XivMessage msg)
        {
            string filePath = DirectoryPath + "/Nameless.json";
            string jsonContent = File.ReadAllText(filePath);
            Nameless = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonContent);

            if (Nameless.ContainsKey(msg.Sentence))
            {
                msg.Speaker = Nameless[msg.Sentence];
            }
            else
            {
                Nameless[msg.Sentence] = msg.Speaker;
                Task.Run(async () => await WriteJSON(filePath, Nameless));
            }
            return msg;
        }

        public XivMessage GetRetainer(XivMessage msg)
        {
            if (Retainers.ContainsKey(msg.Sentence))
            {
                msg.Speaker = Retainers[msg.Sentence];
                msg.VoiceName = "Retainer";
            }
            return msg;
        }

        public string GenerateRandomSuffix()
        {
            System.Random random = new System.Random();
            int randomNumber = random.Next(0, 1000);
            return randomNumber.ToString("D3");
        }
        #endregion


        #region Notifications
        public void Notification(string message, bool announce = true, string explanation = "")
        {
            Task.Run(async () => await ShowNotification(message, announce, explanation));
        }

        public Queue<string> notificationQueue = new Queue<string>();
        string lastNotification = "";
        bool notificationInProgress = false;
        async Task ShowNotification(string _message, bool announce = true, string explanation = "")
        {
            /*
            string message = Regex.Replace(_message, @"[\uE000-\uE0FF]", string.Empty);
            bool initialization = false;

            while (true)
            {
                if (!initialization)
                {
                    if (notificationQueue.Contains(message) || (lastNotification == message && explanation == ""))
                        return;

                    notificationQueue.Enqueue(message);
                    initialization = true;
                }

                if (!notificationInProgress)
                {
                    if (notificationQueue.Peek() == message)
                    {
                        notificationQueue.Dequeue();
                        break;
                    }
                }
                await Task.Delay(100);
            }

            notificationInProgress = true;

            if (announce)
            {
                while (notificationBusy)
                    await Task.Delay(100);
                notificationBusy = true;
            }

            // Put it in the Logs
            
            while (logsContainer.transform.childCount >= 15)
            {
                Destroy(logsContainer.transform.GetChild(0).gameObject);
                
            }


            GameObject go = Instantiate(logPrefab, logsContainer.transform);
            if (explanation != "")
                go.GetComponent<TextMeshProUGUI>().text = explanation;
            else
                go.GetComponent<TextMeshProUGUI>().text = message;

            // Show it as a notification
            if (announce)
            {
                notificationsText.text = message;
                lastNotification = message;
                notifications.SetActive(true);
                await new WaitForSeconds(3f);
                notifications.SetActive(false);
                notificationBusy = false;
            }

            notificationInProgress = false;
            */
        }
        #endregion
    }

}