using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.ObjectModel;

namespace convert
{
    public partial class MainPage : ContentPage, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient = new(); // http-клиент для отправки запросов
        private DateTime _selectedDate = DateTime.Today;
        private string _rateDateText;
        private Dictionary<string, CurrencyRate> _ratesCache = new(); // кэш для хранения курсов валют

        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value)) // загрузка курсов
                {
                    Debug.WriteLine($"Дата изменена: {value}");
                    _ = LoadRatesAsync(); // асинхронная загрузка курсов
                }
            }
        }

        public string RateDateText
        {
            get => _rateDateText;
            set => SetProperty(ref _rateDateText, value);
        }

        public ObservableCollection<string> Currencies { get; } = new(); // список валют

        private string _sourceCurrency;
        public string SourceCurrency
        {
            get => _sourceCurrency;
            set
            {
                if (SetProperty(ref _sourceCurrency, value))
                {
                    if (value == "JPY") // условие для иены
                    {
                        SourceAmount = "100";
                    }
                    CalculateTargetAmount();
                }
            }
        }

        private string _targetCurrency;
        public string TargetCurrency
        {
            get => _targetCurrency;
            set
            {
                if (SetProperty(ref _targetCurrency, value))
                    CalculateTargetAmount();
            }
        }

        private string _sourceAmount;
        public string SourceAmount
        {
            get => _sourceAmount;
            set
            {
                if (SetProperty(ref _sourceAmount, value))
                {
                    if (decimal.TryParse(value, out var amount))
                    {
                        _sourceDecimalAmount = amount;
                        CalculateTargetAmount();
                    }
                    else
                    {
                        _sourceDecimalAmount = 0;
                    }
                }
            }
        }

        private string _targetAmount;
        public string TargetAmount
        {
            get => _targetAmount;
            set => SetProperty(ref _targetAmount, value);
        }

        private decimal _sourceDecimalAmount;

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;
            LoadPreferences();
            _ = LoadRatesAsync(); // загрузка курсов валют
        }

        private async Task LoadRatesAsync() // загрузка курсов валют
        {
            DateTime date = SelectedDate;
            Debug.WriteLine($"Загрузка курсов для даты: {date}");

            while (true)
            {
                var rates = await GetRatesAsync(date); // получение курсов валют
                if (rates != null)
                {
                    _ratesCache = rates; // кэширование курсов
                    RateDateText = $"Курс на {date:dd.MM.yyyy}";

                    UpdateCurrencies();
                    CalculateTargetAmount();

                    SavePreferences();
                    break;
                }
                date = date.AddDays(-1); // загрузка данных за предыдущий день
                if (date < DateTime.Today.AddYears(-1)) // если данных не найдено
                {
                    Debug.WriteLine("Данные на указанную дату недоступны.");
                    RateDateText = "Для выбранной даты курс недоступен";
                    break;
                }
            }
        }

        private async Task<Dictionary<string, CurrencyRate>> GetRatesAsync(DateTime date) // получение курсов валют
        {
            string url = date.Date == DateTime.Today
                ? "https://www.cbr-xml-daily.ru/daily_json.js" // текущие курсы
                : $"https://www.cbr-xml-daily.ru/archive/{date:yyyy'%2F'MM'%2F'dd}/daily_json.js"; // архив данных

            Debug.WriteLine($"Запрос URL: {url}");

            try
            {
                var response = await _httpClient.GetFromJsonAsync<JsonElement>(url);
                if (response.TryGetProperty("Valute", out var valute))
                {
                    var rates = valute.Deserialize<Dictionary<string, CurrencyRate>>() ?? new(); // десериализация

                    foreach (var currency in rates.Values)
                    {
                        if (currency.CharCode == "JPY" && currency.Nominal != 1)
                        {
                            currency.Value /= currency.Nominal;
                            currency.Nominal = 1;
                        }
                    }
                    rates["RUB"] = new CurrencyRate
                    {
                        CharCode = "RUB",
                        Name = "Российский рубль",
                        Value = 1,
                        Nominal = 1
                    };

                    return rates;
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Ошибка загрузки данных: {ex.Message}");
            }

            return null;
        }

        private void UpdateCurrencies()
        {
            var previousSourceCurrency = SourceCurrency;
            var previousTargetCurrency = TargetCurrency;

            Currencies.Clear();
            foreach (var rate in _ratesCache.Values) 
            {
                Currencies.Add(rate.CharCode);
            }


            if (Currencies.Contains(previousSourceCurrency))
                SourceCurrency = previousSourceCurrency;
            if (Currencies.Contains(previousTargetCurrency))
                TargetCurrency = previousTargetCurrency;


            if (string.IsNullOrEmpty(SourceCurrency) || !Currencies.Contains(SourceCurrency))
                SourceCurrency = "USD";
            if (string.IsNullOrEmpty(TargetCurrency) || !Currencies.Contains(TargetCurrency))
                TargetCurrency = "RUB";
        }

        public void CalculateTargetAmount() 
        {
            if (string.IsNullOrEmpty(SourceCurrency) || string.IsNullOrEmpty(TargetCurrency) ||
                !_ratesCache.ContainsKey(SourceCurrency) || !_ratesCache.ContainsKey(TargetCurrency))
            {
                return;
            }

            var sourceRate = _ratesCache[SourceCurrency]; // курс исходной валюты
            var targetRate = _ratesCache[TargetCurrency]; // курс целевой валюты
            decimal result = (_sourceDecimalAmount * sourceRate.Value / sourceRate.Nominal) * targetRate.Nominal / targetRate.Value;

            TargetAmount = result.ToString("F2"); // форматирование результата
        }

        private void LoadPreferences() 
        {
            SelectedDate = Preferences.Get("SelectedDate", DateTime.Today);
            SourceAmount = Preferences.Get("SourceAmount", "1");
        }

        private void SavePreferences()
        {
            Preferences.Set("SelectedDate", SelectedDate);
            Preferences.Set("SourceCurrency", SourceCurrency);
            Preferences.Set("TargetCurrency", TargetCurrency);
            Preferences.Set("SourceAmount", SourceAmount);
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null) 
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CurrencyRate
    {
        public string CharCode { get; set; } // код валюты
        public string Name { get; set; } // название валюты
        public decimal Value { get; set; } // курс валюты
        public decimal Nominal { get; set; } // номинал валюты

        public decimal GetRate(decimal amount)
        {
            return amount * Value / Nominal;
        }
    }
}
