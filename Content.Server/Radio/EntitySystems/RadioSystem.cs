using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Server.Radio.Components;
using Content.Server.VoiceMask;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed class RadioSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
	[Dependency] private readonly IdCardSystem _idCardSystem = default!;

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);
    }

    // Теперь словарь не будет создаваться каждый раз при вызове метода GetColorPlayer
    // // Тут хранятся цвета для должностей. У должности все буквы в нижнем регистре, поэтому заглавными писать ничего не надо. Избегайте повторных ключей
    public static readonly Dictionary<string, string> JobColors = new()
    {
        // Неизвестно
        { "неизвестно", "lime" },
        // Синтетики
        { "борг", "white" },
        { "юнит", "white" },
        { "ии", "white" },
        { "искусственный интеллект", "white" },
        // Кеп
        { "капитан", "yellow" },
        { "врио капитан", "yellow" },
        //ЦК
        { "цк", "yellow" },
        { "центральное командование", "yellow" },
        // Есть реально такая карта у ЦК... Я надеюсь эта должность никогда не понадобится...
        { "неко-горничная", "yellow" },
        { "агент центкома", "yellow" },
        // Главы
        { "адъютант", "#3065e8" },
        { "старший инженер", "#3065e8" },
        { "си", "#3065e8" },
        { "врио старший инженер", "#3065e8" },
        { "врио си", "#3065e8" },
        { "научный руководитель", "#3065e8" },
        { "нр", "#3065e8" },
        { "врио нр", "#3065e8" },
        { "врио научный руководитель", "#3065e8" },
        { "глава персонала", "#3065e8" },
        { "врио глава персонала", "#3065e8" },
        { "врио гп", "#3065e8" },
        { "гп", "#3065e8" },
        { "глава службы безопасности", "#3065e8" },
        { "врио глава службы безопасности", "#3065e8" },
        { "врио гсб", "#3065e8" },
        { "гсб", "#3065e8" },
        { "квартирмейстер", "#3065e8" },
        { "врио квартирмейстер", "#3065e8" },
        { "врио км", "#3065e8" },
        { "км", "#3065e8" },
        { "главный врач", "#3065e8" },
        { "врио главный врач", "#3065e8" },
        { "врио гв", "#3065e8" },
        { "гв", "#3065e8" },
        //ЦК
        { "пцк", "yellow" },
        { "представитель цк", "yellow" },
        { "представитель центком", "yellow" },
        { "представитель центрального командования", "yellow" },
        //СБ
        { "офицер сб", "#ff2727" },
        { "кадет сб", "#ff2727" },
        { "смотритель", "#ff2727" },
        { "детектив", "#ff2727" },
        { "старший офицер", "#ff2727" },
        { "со", "#ff2727" },
        { "бригмед", "#ff2727" },
        { "бригмедик", "#ff2727" },
        { "бм", "#ff2727" },
        //Инжы
        { "инженер станции", "orange" },
        { "инженер", "orange" },
        { "атмосферный техник", "orange" },
        { "атмос", "orange" },
        { "ведущий инженер", "orange" },
        { "ви", "orange" },
        { "технический ассистент", "orange" },
        //РНД
        { "научный ассистент", "mediumpurple" },
        { "учёный", "mediumpurple" },
        { "ученый", "mediumpurple" },
        { "робототехник", "mediumpurple" },
        { "старший научный сотрудник", "mediumpurple" },
        { "снс", "mediumpurple" },
        //Сервис
        { "сервисный работник", "green" },
        { "зоотехник", "green" },
        { "репортёр", "green" },
        { "репортер", "green" },
        { "пассажир", "lime" },
        { "музыкант", "#aaeeaf" },
        { "мим", "#aaeeaf" },
        { "библиотекарь", "#aaeeaf" },
        { "юрист", "#aaeeaf" },
        { "уборщик", "#aaeeaf" },
        { "клоун", "#aaeeaf" },
        { "шеф-повар", "#aaeeaf" },
        { "священник", "#aaeeaf" },
        { "боксёр", "#aaeeaf" },
        { "боксер", "#aaeeaf" },
        { "ботаник", "#aaeeaf" },
        { "бармен", "#aaeeaf" },
        //Карго
        { "грузчик", "#7b3f00" },
        { "утилизатор", "#7b3f00" },
        //Мед
        { "интерн", "skyblue" },
        { "врач", "skyblue" },
        { "доктор", "skyblue" },
        { "химик", "skyblue" },
        { "парамедик", "skyblue" },
        { "хирург", "skyblue" },
        { "патологоанатом", "skyblue" },
        { "психолог", "skyblue" },
        { "ведущий врач", "skyblue" },
        //Юр деп
        { "агент внутренних дел", "pink" },
        { "авд", "pink" },
        { "магистрат", "pink" },
        // ДСО - департамент спецаилбных операций
        { "офицер специальных операций", "#C0C0C0" },
        { "офицер спец операций", "#C0C0C0" },
        { "осо", "#C0C0C0" },
        { "дсо", "#C0C0C0" },
        { "эскадрон смерти", "#C0C0C0" },
        { "эс", "#C0C0C0" },
        { "уборщик обр", "#C0C0C0" },
        { "обр", "#C0C0C0" },
        { "инженер обр", "#C0C0C0" },
        { "служба безопасности обр", "#C0C0C0" },
        { "медик обр", "#C0C0C0" },
        { "лидер обр", "#C0C0C0" },
        { "лидер рхбзз", "#C0C0C0" },
        { "уборщик рхбзз", "#C0C0C0" },
        { "рхбзз", "#C0C0C0" },
        { "инженер рхбзз", "#C0C0C0" },
        { "служба безопасности рхбзз", "#C0C0C0" },
        { "медик рхбзз", "#C0C0C0" },
        //ОСЩ
        { "офицер синий щит", "#C0C0C0" },
        { "офицер \"синий щит\"", "#C0C0C0" },
        { "офицер «синий щит»", "#C0C0C0" },
        { "осщ", "#C0C0C0" },
        { "синий щит", "#C0C0C0" }
    };


	/// <summary>
    /// Для EnityUid ищется карта в руках или в пда. Если карта нашлась, мы смотрим какая там работа и достаём её
    /// </summary>
    /// <param name="uid"></param>
    /// <returns></returns>
    private string? GetJobPlayer(EntityUid uid)
    {
        // Проверяем нашлась ли карта и какое id у карты
        if (_idCardSystem.TryFindIdCard(uid, out var id))
        {
            // Мы сохраняем работу
            string? playerJob = id.Comp.JobTitle;
            // Если работа нашлась верно, мы начинаем основной процесс
            if (playerJob != null)
            {
                // Перевод для некоторых ролей, которые у нас не переведены
                if (playerJob == "Central Commander")
                {
                    playerJob = $"Центральное Командование";
                }

                // Перевод 2
                if (playerJob == "Centcom Quarantine Officer")
                {
                    playerJob = $"Офицер Специальных Операций";
                }

                // Делаем начало должности с заглавной буквы и сохраняем в playerJob
                playerJob = char.ToUpper(playerJob[0]) + playerJob.Substring(1);
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
    /// <param name="jobPlayer"></param>
    /// <returns></returns>
    private string? GetColorPlayer(string? jobPlayer)
    {
        // Проверка. Работаем только тогда, когда работа была определена успешно
        if (jobPlayer != null)
        {
            // Преобразуем jobPlayer к нижнему регистру для поиска в словаре
            string normalizedJob = jobPlayer.ToLower();

            // Ищем цвет по нормализованному значению должности
            string color = JobColors.TryGetValue(normalizedJob, out var jobColor) ? jobColor : "lime";
            return color;

        }
        // На всякий случай проверка ещё раз
        return null;
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(EntityUid messageSource, string message, ProtoId<RadioChannelPrototype> channel, EntityUid radioSource, bool escapeMarkup = true)
    {
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    /// <param name="messageSource">Entity that spoke the message</param>
    /// <param name="radioSource">Entity that picked up the message and will send it, e.g. headset</param>
    public void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, bool escapeMarkup = true)
    {
		// Активируем оба наших метода ОБЯЗАТЕЛЬНО в данном порядке
        string? jobPlayer = GetJobPlayer(messageSource);
        string? color = GetColorPlayer(jobPlayer);

        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        var name = TryComp(messageSource, out VoiceMaskComponent? mask) && mask.Enabled
            ? mask.VoiceName
            : MetaData(messageSource).EntityName;

		// Если должность определена, добавляем
        if (jobPlayer != null)
        {
            // Добавляем должность. Важно это сделать перед FormattedMessage
            name = $"[{jobPlayer}] {name}";
        }

        name = FormattedMessage.EscapeText(name);

		// Если всё удачно определилось, мы присваиваем и цвет должности
        if ((color != null) && (jobPlayer != null))
        {
            // Мы получили необходимый результат
            name = $"[bold][color={color}]{name}[/color][/bold]";
        }

        SpeechVerbPrototype speech;
        if (mask != null
            && mask.Enabled
            && mask.SpeechVerb != null
            && _prototype.TryIndex<SpeechVerbPrototype>(mask.SpeechVerb, out var proto))
        {
            speech = proto;
        }
        else
            speech = _chat.GetSpeechVerb(messageSource, message);

        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        var wrappedMessage = Loc.GetString(speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap",
            ("color", channel.Color),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", name),
            ("message", content));

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var chat = new ChatMessage(
            ChatChannel.Radio,
            message,
            wrappedMessage,
            NetEntity.Invalid,
            null);
        var chatMsg = new MsgChatMessage { Message = chat };
        var ev = new RadioReceiveEvent(message, messageSource, channel, chatMsg);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var hasMicro = HasComp<RadioMicrophoneComponent>(radioSource);

        var speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && (!hasMicro || !speakerQuery.HasComponent(receiver));
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // send the message
            RaiseLocalEvent(receiver, ref ev);
        }

        // Так как теперь у нас name отличается, необходимо сделать дополнительные проверки, чтобы убедиться что это не мой код вошёл в исключения, а какой-то баг активировал код
        if ((name != Name(messageSource)) || (name != $"[b][color={color}][{jobPlayer}] {Name(messageSource)}[/color][/b]"))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(chat);
        _messages.Remove(message);
    }

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
