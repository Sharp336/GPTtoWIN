using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;

namespace wtp
{
    public class ConfigurationService
    {
        private const string JsonFileName = "config.json";

        private readonly string _jsonPath;

        public Config Config { get; private set; } = new Config();

        public Profile DefaultProfile()
        {
            var defaultProfile = new Profile();
            try
            {
                defaultProfile = Config.Profiles.Single(pr => pr.ProfileName == Config.DefaultProfileName);
            }
            catch (Exception e) {
                Debug.WriteLine(e);
                HandleInvalidConfig();
            }
            return defaultProfile;
        }

        public ConfigurationService()
        {
            _jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, JsonFileName);
            Load();
        }

        public Config Load()
        {
            try
            {
                if (File.Exists(_jsonPath))
                {
                    string json = File.ReadAllText(_jsonPath);
                    var config = JsonConvert.DeserializeObject<Config>(json);
                    if (config != null)
                    {
                        Config = config; // Используйте десериализованные данные
                    }
                    else
                    {
                        Debug.WriteLine($"Configuration cannot be deserilized properly");
                        // Обработка некорректной конфигурации без перезаписи файла
                        HandleInvalidConfig();
                    }
                }
                else
                {
                    Config.Profiles.Add(new Profile() { ProfileName = "Default profile", PromptRegexps = new List<PromptRegexp> { new PromptRegexp() { Name = "Windows default", Regex = @"[a-zA-Z]:\\[^>]+>" }, new PromptRegexp() { Name = "Yes/No", Regex = @".*\(Yes\/No\)" } } });
                    Save(); // Файл конфигурации не существует, создаем его с дефолтными значениями
                }
            }
            catch(Exception e)
            {
                Debug.WriteLine(e);
                // Обработка ошибок десериализации без перезаписи файла
                HandleInvalidConfig();
            }
            return Config;
        }


        private void HandleInvalidConfig()
        {
            var result = MessageBox.Show("The configuration file is incorrect or missing. Would you like to open it for editing or reset to default settings? Click 'Yes' to edit, 'No' to reset.", "Configuration Error", MessageBoxButton.YesNoCancel, MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                // Попытка открыть файл конфигурации для редактирования
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_jsonPath) { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("Failed to open the configuration file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (result == MessageBoxResult.No)
            {
                // Сброс конфигурации к дефолтным настройкам и сохранение
                Save();
            }

            // Завершение работы приложения в любом случае после обработки выбора пользователя
            Environment.Exit(0);
        }


        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_jsonPath, json);
            }
            catch
            {
#if DEBUG
                throw;
#endif
            }
        }
    }
}
