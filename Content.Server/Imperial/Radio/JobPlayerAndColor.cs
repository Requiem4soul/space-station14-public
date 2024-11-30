using System.Text.RegularExpressions;
using Content.Server.Access.Systems;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;
using Content.Shared.Imperial.Radio;

namespace Content.Server.Imperial.Radio;

/// <summary>
/// Данный класс отвечает за отображения должностей в канале рации. То есть, если ошибка в Content.Radi.EnitySystem - вам сюда. Так же в самом оригинальном коде рации есть способ быстро востановить его оригинальную работу без моего класса
/// </summary>
public sealed class JobPlayerAndColor : EntitySystem
{
    // Для работы с id картой
    [Dependency] private readonly IdCardSystem _idCardSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    // Стреляй, О(1) не отдам
    private static Dictionary<string, string> SaveColorForJob = new Dictionary<string, string>();

    // Метод, который проверяет нужно ли заполнять словарь
    public void IsDictionaryEmpty()
    {
        // Проверяем, пуст ли словарь, если пустой — инициализируем
        if (SaveColorForJob.Count == 0)
        {
            LoadPrototypes();
        }
    }

    // Метод, который заполняет словарь. Вызывается только через IdColorService
    public void LoadPrototypes()
    {
        // Получаем все прототипы для idColor
        var prototypes = IoCManager.Resolve<IPrototypeManager>().EnumeratePrototypes<IdColorAndJobPrototype>();

        foreach (var proto in prototypes)
        {
            // Проверяем, что id и color в прототипах не будет иметь значения null, так как у нас храняться только string, а не string?
            if (!string.IsNullOrEmpty(proto.ID) && !string.IsNullOrEmpty(proto.Color))
            {
                // Добавляем в словарь только уникальные ID
                if (!SaveColorForJob.ContainsKey(proto.ID))
                {
                    SaveColorForJob.Add(proto.ID, proto.Color);
                }
            }
        }
    }

    // Словарь для перевода должностей
        private static readonly Dictionary<string, string> JobTranslations = new()
        {
            { "Central Commander", "Центральное Командование" },
            { "Centcom Quarantine Officer", "РХБЗЗ" }
        };

        private string TranslateJob(string job)
        {
            // Ищем перевод в словаре
            if (JobTranslations.TryGetValue(job, out var translatedJob))
            {
                return translatedJob;
            }

            // Возвращаем оригинал, если перевода нет
            return job;
        }

	/// <summary>
    /// Для EnityUid ищется карта в руках или в пда. Если карта нашлась, мы смотрим какая там работа и достаём её
    /// </summary>
    /// <param name="uid"> uid игрока, у которого мы ищем карту</param>
    /// <returns></returns>
    public string? GetJobPlayer(EntityUid uid)
        {
            // Проверяем нашлась ли карта и какое id у карты
            if (_idCardSystem.TryFindIdCard(uid, out var id))
            {
                // Мы сохраняем работу
                string? playerJob = id.Comp.JobTitle;
                // Если работа нашлась верно, мы начинаем основной процесс
                if (playerJob != null)
                {
                    // Делаем начало должности с заглавной буквы и сохраняем в playerJob
                    playerJob = char.ToUpper(playerJob[0]) + playerJob.Substring(1);
                    // Заменяем тире, дефисы и минусы на пробелы. Эти символы при форматировании ломают вывод
                    playerJob = Regex.Replace(playerJob, @"[-–—−]", " ");
                    // Уберём лишние символы "!?", которые могут ломать в целом вывод сообщения в радио канале
                    playerJob = Regex.Replace(playerJob, @"[^a-zA-Zа-яА-ЯёЁ ]", "");
                    // Переводим должность через словарь. Если перевода нет, playerJob не меняется
                    playerJob = TranslateJob(playerJob);
                    // Убрав лишние пробелы, передаём полученное значение
                    return playerJob.Trim();

                }
            }
            // Если работы нет, то возвращается должность "Неизвестно"
            return "Неизвестно";
        }

    /// <summary>
    /// Метод, который отвечает за подбор цвета для должности. Используется словарь, который работает по О(1), что быстрее if и подобного
    /// </summary>
    /// <param name="jobPlayer"> Нужно получить из метода GetJobPlayer. Или же вставляйте сюда свою string? работу, но нужно привести её к нижниму регистру для словаря. Метод ваш_стринг.ToLower()</param>
    /// <returns></returns>
    public string? GetColorPlayer(string? jobPlayer)
    {
        // Проверка. Работаем только тогда, когда работа была определена успешно
        if (jobPlayer != null)
        {
            // Преобразуем jobPlayer к нижнему регистру для поиска в словаре
            string normalizedJob = jobPlayer.ToLower();

            // Выполняется по О(1). Если вдруг будет очистка памяти и словарь удалится, то мы будем уверены что он снова инициализируется
            IsDictionaryEmpty();

            // Проверка наличия должности в словаре
            if (SaveColorForJob.ContainsKey(normalizedJob))
            {
                // Если должность найдена в словаре, возвращаем соответствующий цвет
                return SaveColorForJob[normalizedJob];
            }
        }

        // null не стоит собственно ручно возвращать, исправил на более стабильное значение
        return "lime";
    }

    /// <summary>
    /// Данный метод, используя два прошлых метода, формирует уже необходимый name, который и будет отображён в рации. На самом деле, его можно применять и к ChatSystem.cs
    /// </summary>
    /// <param name="uid"> id отправителя. В оригинальном коде рации messageSource</param>
    /// <param name="name"> string?. Это по сути то, что будет указано в отправителе. Раньше там писалось просто имя</param>
    /// <returns></returns>
    public string CompletedJobAndPlayer(EntityUid uid,  string? name)
    {
        // Если игрок как-то умудрился ввести null в name, то мы делаем Неизвестное имя
        if (string.IsNullOrEmpty(name))
        {
            name = "Неизвестно";
        }

        // Активируем метод для определения должности
        string? nameJobPlayer = GetJobPlayer(uid);
        // Если должность определена, добавляем
        if (!string.IsNullOrEmpty(nameJobPlayer))
        {
            // Добавляем должность. Важно это сделать перед FormattedMessage
            name = $"[{nameJobPlayer}] {name}";
        }
        else
        {
            // Необходимая проверка от крашей
            name = $"[Неизвестно] {name}";
        }

        // Это необходимость, которая присутствует в оригинальном коде рации. До неё color и болд не будут работать. А после неё уже не добавить текст, не будет отображаться в рации
        name = FormattedMessage.EscapeText(name);

        // Определяем какой цвет должен быть у данной должности
        string? nameColorPlayer = GetColorPlayer(nameJobPlayer);

        if (string.IsNullOrEmpty(nameColorPlayer))
        {
            // Проверка от крашей
            nameColorPlayer = "lime";
        }

        // В основном, будет работать данный return в if
        if (!string.IsNullOrEmpty(nameColorPlayer) && !string.IsNullOrEmpty(nameJobPlayer))
        {
            // Тут идёт формирование как раз необходимого имени с должностью и цветом
            string? nameEnd = $"[bold][color={nameColorPlayer}][{nameJobPlayer}] {name}[/bold][/color]";
            // Возвращаем
            return nameEnd;
        }

        // Если произошло второе пришествие, мы всё равно вернём корректные значения, которые не вызовут краша игры
        return $"[bold][color=lime]{name}[/bold][/color]";
    }
}

