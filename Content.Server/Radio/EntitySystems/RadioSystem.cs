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
            // Тут хранятся цвета для должностей. У должности все буквы в нижнем регистре, поэтому заглавными писать ничего не надо. Избегайте повторных ключей
            var jobColors = new Dictionary<string, string>
            {
                { "неизвестно", "lime" },
                { "борг", "white" },
                { "юнит", "white" },
                { "ии", "white" },
                { "искусственный интеллект", "white" },
                { "капитан", "yellow" },
                { "врио капитан", "yellow" },
                { "цк", "yellow" },
                { "центральное командование", "yellow" },
                { "агент центкома", "yellow" },
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
                { "пцк", "yellow" },
                { "представитель цк", "yellow" },
                { "представитель центком", "yellow" },
                { "представитель центрального командования", "yellow" },
                { "офицер сб", "red" },
                { "кадет сб", "red" },
                { "смотритель", "red" },
                { "детектив", "red" },
                { "старший офицер", "red" },
                { "бригмед", "red" },
                { "бригмедик", "red" },
                { "бм", "red" },
                { "инженер станции", "orange" },
                { "инженер", "orange" },
                { "атмосферный техник", "orange" },
                { "атмос", "orange" },
                { "ведущий инженер", "orange" },
                { "ви", "orange" },
                { "технический ассистент", "orange" },
                { "научный ассистент", "mediumpurple" },
                { "учёный", "mediumpurple" },
                { "ученый", "mediumpurple" },
                { "робототехник", "mediumpurple" },
                { "старший научный сотрудник", "mediumpurple" },
                { "снс", "mediumpurple" },
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
                { "грузчик", "sandybrown" },
                { "утилизатор", "sandybrown" },
                { "интерн", "skyblue" },
                { "врач", "skyblue" },
                { "химик", "skyblue" },
                { "парамедик", "skyblue" },
                { "хирург", "skyblue" },
                { "патологоанатом", "skyblue" },
                { "психолог", "skyblue" },
                { "ведущий врач", "skyblue" },
                { "агент внутренних дел", "pink" },
                { "авд", "pink" },
                { "магистрат", "pink" },
                { "офицер специальных операций", "#7d1010" },
                { "офицер спец операций", "#7d1010" },
                { "осо", "#7d1010" },
                { "дсо", "#7d1010" },
                { "эскадрон смерти", "#7d1010" },
                { "эс", "#7d1010" },
                { "уборщик обр", "#7d1010" },
                { "обр", "#7d1010" },
                { "инженер обр", "#7d1010" },
                { "служба безопасности обр", "#7d1010" },
                { "медик обр", "#7d1010" },
                { "лидер обр", "#7d1010" },
                { "лидер рхбзз", "#7d1010" },
                { "уборщик рхбзз", "#7d1010" },
                { "рхбзз", "#7d1010" },
                { "инженер рхбзз", "#7d1010" },
                { "служба безопасности рхбзз", "#7d1010" },
                { "медик рхбзз", "#7d1010" },
                { "офицер синий щит", "#7d1010" },
                { "офицер \"синий щит\"", "#7d1010" },
                { "осщ", "#7d1010" },
                { "синий щит", "#7d1010" }
            };
            // Преобразуем jobPlayer к нижнему регистру для поиска в словаре
            string normalizedJob = jobPlayer.ToLower();

            // Ищем цвет по нормализованному значению должности
            string color = jobColors.TryGetValue(normalizedJob, out var jobColor) ? jobColor : "lime";
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
            name = $"[b][color={color}]{name}[/color][/b]";
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
        if ((name != Name(messageSource)) && (name != $"[b][color={color}][{jobPlayer}] {Name(messageSource)}[/color][/b]") && ((jobPlayer == null) && (color == null) && (name != Name(messageSource)) ))
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
