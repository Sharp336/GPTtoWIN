using System;
using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace tterm
{
    internal class ConfigurationService
    {
        private const string JsonFileName = "config.json";

        private readonly string _jsonPath;

        public Config Config { get; private set; } = new Config();

        public ConfigurationService()
        {
            _jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, JsonFileName);
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
                        Config = config;
                        return Config;
                    }
                    else
                    {
                        HandleInvalidConfig(); // Обработка некорректной конфигурации
                    }
                }
                else
                {
                    Save(); // Создание файла с дефолтной конфигурацией
                }
            }
            catch
            {
                HandleInvalidConfig(); // Обработка ошибок десериализации
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
