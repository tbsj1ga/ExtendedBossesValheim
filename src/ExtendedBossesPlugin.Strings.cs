using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ExtendedBosses
{
    // Every player-facing text in two languages. A message is sent as a key + arguments and each
    // client renders it in its own language:
    //   * players WITH the mod get the key through our RPC and pick Russian or English from their
    //     game language (Localization.GetSelectedLanguage);
    //   * players WITHOUT the mod can only receive the vanilla ShowMessage, whose text is fixed
    //     by the sender - 01 General/GuestLanguage picks it (Russian, English or both).
    // Boss and creature names are passed as vanilla tokens ($enemy_gdking): ShowMessage runs
    // Localization.Localize on the receiving client, so even players without the mod see names
    // in their own language. The server knows who has the mod: clients say hello on connect.
    public partial class ExtendedBossesPlugin
    {
        private static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
        {
            // ---- Eikthyr
            { "eikthyr.80", new[] { "{0}: Herd, to me!", "{0}: Стадо, ко мне!" } },
            { "eikthyr.60", new[] { "{0} lowers the antlers - beware the charge!", "{0} пригибает рога — берегитесь рывка!" } },
            { "eikthyr.50", new[] { "{0}: Rise, bones! (smash the skull pile)", "{0}: Кости, встаньте! (разбейте груду черепов)" } },
            { "eikthyr.30", new[] { "{0}: The sky answers me! (the marked one - move away from the others)", "{0}: Небо, ответь мне! (отмеченный — отойди от остальных)" } },
            { "eikthyr.15", new[] { "{0} calls the leader of the herd!", "{0} призывает вожака стада!" } },
            { "eikthyr.mark", new[] { "{0} marks {1} for lightning - spread out!", "{0} метит молнией: {1} — разойдитесь!" } },

            // ---- The Elder
            { "elder.85", new[] { "{0}: Forest, hear me! (destroy the nest)", "{0}: Лес, услышь меня! (разрушьте гнездо)" } },
            { "elder.70", new[] { "{0}: More of my children! (nests - and roots stir underfoot)", "{0}: Ещё мои дети! (гнёзда — и корни под ногами)" } },
            { "elder.55", new[] { "{0}: My nests shield me! (destroy them all)", "{0}: Гнёзда укроют меня! (разрушьте их все)" } },
            { "elder.bark.sap", new[] { "{0}: the bark swells with sap - fire is useless, chop with axes!", "{0}: кора набухла соком — огонь бесполезен, рубите топорами!" } },
            { "elder.bark.back", new[] { "{0} is covered in living bark - only blows to the back hurt! Tank, hold it facing you!", "{0}: живая кора — ранят только удары в спину! Танк, держите босса лицом к себе!" } },
            { "elder.bark.end", new[] { "{0}: the bark cracks - hit with anything!", "{0}: кора трескается — бейте чем угодно!" } },
            { "elder.45", new[] { "{0} calls a troll!", "{0} призывает тролля!" } },
            { "elder.30", new[] { "{0} wakes a bear!", "{0} будит медведя!" } },
            { "elder.20", new[] { "{0} calls the elite - kill the shamans first, they heal the boss!", "{0} зовёт элиту — сначала убейте шаманов, они лечат босса!" } },
            { "elder.10", new[] { "{0}: Dead of the barrows, rise!", "{0}: Мёртвые курганов, встаньте!" } },
            { "elder.mark", new[] { "Roots stir under {1} - get out!", "Корни шевелятся под игроком {1} — выбирайтесь!" } },
            { "elder.seed", new[] { "A seed of {0} takes root - a new nest!", "Семя ({0}) пустило корни — новое гнездо!" } },

            // ---- Bonemass
            { "bonemass.85", new[] { "{0}: The swamp remembers its dead! (destroy the bone pile)", "{0}: Болото помнит своих мертвецов! (разрушьте кучу костей)" } },
            { "bonemass.70", new[] { "{0}: More bones! (piles - poison underfoot - leeches in the water)", "{0}: Больше костей! (кучи — яд под ногами — пиявки в воде)" } },
            { "bonemass.55", new[] { "{0}: The bones shield me! (destroy all the piles)", "{0}: Кости укроют меня! (разрушьте все кучи)" } },
            { "bonemass.harden", new[] { "{0}: the bones harden - blunt is useless, burn it!", "{0}: кости затвердели — дробящее бесполезно, жгите!" } },
            { "bonemass.harden.end", new[] { "{0}: the bones soften again", "{0}: кости снова размякли" } },
            { "bonemass.rot", new[] { "{0}: rotten steam - close blows sink in and poison you, strike from afar!", "{0}: гнилостный пар — удары вблизи вязнут и травят, бейте издалека!" } },
            { "bonemass.rot.end", new[] { "{0}: the steam clears", "{0}: пар рассеялся" } },
            { "bonemass.45", new[] { "{0} raises an abomination! Slime will crawl to the boss - kill it before it merges!", "{0} поднимает мерзость! К боссу поползёт слизь — убейте её, пока не слилась!" } },
            { "bonemass.fusion", new[] { "{0} calls the slime - intercept it!", "{0} зовёт слизь — перехватите её!" } },
            { "bonemass.40", new[] { "{0}: Writhans, to me!", "{0}: Скручни, ко мне!" } },
            { "bonemass.35", new[] { "{0}: Fire of the swamp, come! (surtlings)", "{0}: Огонь болот, ко мне! (суртлинги)" } },
            { "bonemass.25", new[] { "{0} raises the elite dead!", "{0} поднимает элитных драугров!" } },
            { "bonemass.15", new[] { "{0}: Spirits of the swamp, feast!", "{0}: Духи болота, пируйте!" } },
            { "bonemass.mark", new[] { "{0} spits poison under {1} - spread out!", "{0} плюёт ядом под игрока {1} — разойдитесь!" } },

            // ---- Moder
            { "moder.85", new[] { "{0}: The ice will bear my brood! (break the ice spike) - the pack is coming!", "{0}: Лёд выносит моё потомство! (разбейте ледяной сталагмит) — идёт стая!" } },
            { "moder.70", new[] { "{0}: More ice, more young! (spikes - frost flashes under your feet)", "{0}: Больше льда — больше детёнышей! (сталагмиты — ледяные вспышки под ногами)" } },
            { "moder.55", new[] { "{0}: The ice shields me! (break every spike)", "{0}: Лёд укроет меня! (разбейте все сталагмиты)" } },
            { "moder.ice", new[] { "{0}: ice armor - arrows bounce off while she is on the ground, fight up close!", "{0}: ледяная броня — пока она на земле, стрелы отскакивают, бейте вблизи!" } },
            { "moder.ice.end", new[] { "{0}: the ice armor melts", "{0}: ледяная броня тает" } },
            { "moder.thorns", new[] { "{0}: ice thorns - every blow is paid back with frost, hit with care!", "{0}: ледяные шипы — каждый удар возвращается морозом, бейте с умом!" } },
            { "moder.thorns.end", new[] { "{0}: the thorns break off", "{0}: шипы обламываются" } },
            { "moder.45", new[] { "{0} calls the cultists - their fire burns her ice too!", "{0} зовёт культистов — их огонь жжёт и её лёд!" } },
            { "moder.35", new[] { "{0} wakes the stone golem!", "{0} будит каменного голема!" } },
            { "moder.20", new[] { "{0}: All my brood, to me!", "{0}: Всё моё потомство — ко мне!" } },
            { "moder.mark", new[] { "{0} breathes frost under {1} - spread out!", "{0} дышит морозом под игрока {1} — разойдитесь!" } },
            { "moder.breath", new[] { "{0} lands and fixes her eyes on {1} - the rest, stay out of her breath!", "{0} садится и смотрит на игрока {1} — остальные, не стойте под её дыханием!" } },

            // ---- Yagluth
            { "yagluth.85", new[] { "{0}: My tribe, rise! (break the fuling totem)", "{0}: Моё племя, встань! (сломайте тотем фулингов)" } },
            { "yagluth.70", new[] { "{0}: Burn! (more totems - meteors fall on the marked)", "{0}: Горите! (ещё тотемы — метеоры падают на отмеченных)" } },
            { "yagluth.55", new[] { "{0}: The totems shield me, the shamans mend me! (break the totems, kill the shamans)", "{0}: Тотемы укроют, шаманы исцелят! (сломайте тотемы, убейте шаманов)" } },
            { "yagluth.regen", new[] { "{0} feeds on fire - he heals unless chilled! Hit with frost!", "{0} питается огнём — он лечится, пока его не остудят! Бейте морозом!" } },
            { "yagluth.regen.end", new[] { "{0}: the fire inside dims", "{0}: огонь внутри угасает" } },
            { "yagluth.adapt", new[] { "{0} adapts: {1} hurts him less - switch weapons!", "{0} приспособился: урон «{1}» проходит слабее — смените оружие!" } },
            { "yagluth.adapt.end", new[] { "{0} loses the adaptation", "{0}: приспособление прошло" } },
            { "yagluth.45", new[] { "{0} calls the brutes!", "{0} зовёт брютов!" } },
            { "yagluth.30", new[] { "{0} wakes the Unbjorn!", "{0} будит анбьёрна!" } },
            { "yagluth.20", new[] { "{0}: Bones of my servants, rise!", "{0}: Кости моих слуг, встаньте!" } },
            { "yagluth.15", new[] { "{0}: Swarm, drink their blood!", "{0}: Рой, пей их кровь!" } },
            { "yagluth.10", new[] { "{0} calls an echo of the dragon queen!", "{0} призывает эхо королевы драконов!" } },
            { "yagluth.mark", new[] { "{0} calls meteors on {1} - run!", "{0} зовёт метеоры на игрока {1} — бегите!" } },
            { "yagluth.beam", new[] { "{0} locks his gaze on {1} - break line of sight!", "{0} не сводит глаз с игрока {1} — укройтесь от луча!" } },

            // ---- The Queen
            { "queen.85", new[] { "{0} lays a clutch! (smash the eggs before they hatch)", "{0} откладывает кладку! (разбейте яйца, пока не вылупились)" } },
            { "queen.70", new[] { "{0}: More children! (eggs - acid falls on the marked)", "{0}: Больше детей! (яйца — кислота на отмеченных)" } },
            { "queen.55", new[] { "{0}: My brood shields me! (smash every egg)", "{0}: Выводок укроет меня! (разбейте все яйца)" } },
            { "queen.blood", new[] { "{0} thirsts - her blows heal her! Dodge, block, keep away!", "{0} жаждет крови — её удары лечат её! Уклоняйтесь, блокируйте, не подставляйтесь!" } },
            { "queen.blood.end", new[] { "{0}: the thirst fades", "{0}: жажда утихает" } },
            { "queen.acid", new[] { "{0}: acid thorns - every blow is paid back with poison, hit with care!", "{0}: кислотные шипы — каждый удар возвращается ядом, бейте с умом!" } },
            { "queen.acid.end", new[] { "{0}: the acid dries up", "{0}: кислота высыхает" } },
            { "queen.45", new[] { "{0} calls her guards!", "{0} зовёт стражей!" } },
            { "queen.30", new[] { "{0} calls a gjall from the mist!", "{0} призывает гьялля из тумана!" } },
            { "queen.20", new[] { "{0}: All my brood, devour them!", "{0}: Весь выводок — пожрите их!" } },
            { "queen.mark", new[] { "{0} spits acid at {1} - spread out!", "{0} плюёт кислотой в игрока {1} — разойдитесь!" } },
            { "queen.cocoon", new[] { "{0} hides behind her guards - kill them to reach her!", "{0} укрылась за стражами — убейте их, чтобы добраться до неё!" } },

            // ---- Fader
            { "fader.85", new[] { "{0}: Rise, charred! (break the spawner stone)", "{0}: Встаньте, обугленные! (разбейте камень-спавнер)" } },
            { "fader.70", new[] { "{0}: The sky burns! (more stones - meteors fall on the marked)", "{0}: Небо горит! (ещё камни — метеоры падают на отмеченных)" } },
            { "fader.55", new[] { "{0}: The stones shield me! (break them all)", "{0}: Камни укроют меня! (разбейте их все)" } },
            { "fader.molten", new[] { "{0}: molten armor - close blows sink in and burn you, strike from afar!", "{0}: раскалённая броня — удары вблизи вязнут и обжигают, бейте издалека!" } },
            { "fader.molten.end", new[] { "{0}: the armor cools", "{0}: броня остывает" } },
            { "fader.ash", new[] { "{0}: ash veil - arrows and spells get lost, fight up close!", "{0}: пепельная завеса — стрелы и заклинания теряются, бейте вблизи!" } },
            { "fader.ash.end", new[] { "{0}: the ash settles", "{0}: пепел оседает" } },
            { "fader.45", new[] { "{0} wakes a morgen!", "{0} будит моргена!" } },
            { "fader.30", new[] { "{0} calls a fallen valkyrie!", "{0} призывает падшую валькирию!" } },
            { "fader.20", new[] { "{0} calls Lord Reto!", "{0} призывает лорда Рето!" } },
            { "fader.10", new[] { "{0}: Ashlands, devour them!", "{0}: Пепельные земли, пожрите их!" } },
            { "fader.mark", new[] { "{0} calls meteors on {1} - run!", "{0} зовёт метеоры на игрока {1} — бегите!" } },
            { "fader.wall", new[] { "{0} raises a wall of fire - split up!", "{0} поднимает стену огня — разделитесь!" } },

            // ---- common
            { "window", new[] { "{0}: the shield has fallen - strike now!", "{0}: защита пала — бейте!" } },
            { "reset", new[] { "{0} regains strength...", "{0} восстанавливает силы…" } },

            // ---- console
            { "cmd.help", new[] { "eb status | check | phase <n> | reset | probe <prefab> | players <n|0>", "eb status | check | phase <n> | reset | probe <префаб> | players <n|0>" } },
            { "cmd.noboss", new[] { "No boss owned by this client nearby (the mechanics run on the owner).", "Рядом нет босса, которым владеет этот клиент (механики работают у владельца)." } },
            { "cmd.reset", new[] { "{0}: fight reset.", "{0}: бой сброшен." } },
            { "cmd.phase", new[] { "{0}: phase {1} ({2}%) forced.", "{0}: фаза {1} ({2} %) запущена." } },
            { "cmd.phaseusage", new[] { "eb phase <0..{0}>", "eb phase <0..{0}>" } },
            { "cmd.players", new[] { "eb players <n>  (0 = count real players)", "eb players <n>  (0 — считать реальных игроков)" } },
            { "cmd.playerscount", new[] { "Group size: counted.", "Размер группы: считается по игрокам." } },
            { "cmd.playersforced", new[] { "Group size forced to {0} (this client only).", "Размер группы принудительно {0} (только на этом клиенте)." } },
            { "cmd.inert", new[] { "{0} is inert after errors (see the log).", "{0} отключился после ошибок (см. лог)." } },
            { "cmd.noworld", new[] { "Not in a world.", "Не в мире." } },
            { "cmd.nobosses", new[] { "No known boss loaded nearby.", "Рядом нет загруженных боссов мода." } },
            { "cmd.status", new[] { "{0}: {1}/{2}, HP {3}%, phases {4}/{5}, {6}, group {7}, damage x{8}, adds {9}, nests {10}{11}",
                                    "{0}: {1}/{2}, HP {3} %, фазы {4}/{5}, {6}, группа {7}, урон x{8}, адды {9}, гнёзда {10}{11}" } },
            { "cmd.ownedhere", new[] { "owned here", "владелец — этот клиент" } },
            { "cmd.ownedother", new[] { "owned by another client", "владелец — другой клиент" } },
            { "cmd.shield", new[] { ", SHIELD", ", ЩИТ" } },
            { "cmd.window", new[] { ", WINDOW", ", ОКНО" } },
            { "cmd.cocoon", new[] { ", COCOON", ", КОКОН" } },
            { "cmd.fixate", new[] { ", FIXATED", ", ФИКСАЦИЯ" } },
            { "cmd.resist", new[] { ", RESIST", ", СОПРОТИВЛЕНИЕ" } },
            { "cmd.cycle.sap", new[] { ", BARK (sap)", ", КОРА (сок)" } },
            { "cmd.cycle.back", new[] { ", BARK (back)", ", КОРА (спина)" } },
            { "cmd.cycle.harden", new[] { ", HARDENED", ", ЗАТВЕРДЕЛ" } },
            { "cmd.cycle.rot", new[] { ", ROTTEN STEAM", ", ГНИЛОСТНЫЙ ПАР" } },
            { "cmd.cycle.icearmor", new[] { ", ICE ARMOR", ", ЛЕДЯНАЯ БРОНЯ" } },
            { "cmd.cycle.thorns", new[] { ", ICE THORNS", ", ЛЕДЯНЫЕ ШИПЫ" } },
            { "cmd.cycle.regen", new[] { ", REGENERATION", ", РЕГЕНЕРАЦИЯ" } },
            { "cmd.cycle.adapt", new[] { ", ADAPTED", ", АДАПТАЦИЯ" } },
            { "cmd.cycle.bloodthirst", new[] { ", BLOODTHIRST", ", КРОВОЖАДНОСТЬ" } },
            { "cmd.cycle.acidthorns", new[] { ", ACID THORNS", ", КИСЛОТНЫЕ ШИПЫ" } },
            { "cmd.cycle.molten", new[] { ", MOLTEN ARMOR", ", РАСКАЛЁННАЯ БРОНЯ" } },
            { "cmd.cycle.ashveil", new[] { ", ASH VEIL", ", ПЕПЕЛЬНАЯ ЗАВЕСА" } },
            { "cmd.checkdone", new[] { "Self-check: {0} prefab(s) OK, {1} problem(s) - details in the log.", "Самопроверка: {0} префаб(ов) в порядке, проблем: {1} — подробности в логе." } },
        };

        private ConfigEntry<string> _cfgGuestLanguage;

        internal static bool ClientIsRussian()
        {
            try
            {
                Localization l = Localization.instance;
                return l != null && l.GetSelectedLanguage() == "Russian";
            }
            catch { return false; }
        }

        // Text in one language (0 = English, 1 = Russian); unknown key -> the key itself.
        internal static string T(int lang, string key, params string[] args)
        {
            string[] pair;
            string fmt = Texts.TryGetValue(key, out pair) ? pair[Mathf.Clamp(lang, 0, 1)] : key;
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(fmt, args); }
            catch (FormatException) { return fmt; }
        }

        // Text in this client's language, tokens resolved.
        internal static string L(string key, params string[] args)
        {
            string s = T(ClientIsRussian() ? 1 : 0, key, args);
            Localization loc = Localization.instance;
            return loc != null ? loc.Localize(s) : s;
        }

        // Text for players without the mod (tokens are left for their client to resolve).
        private string GuestText(string key, string[] args)
        {
            string g = Ss(_cfgGuestLanguage);
            if (g == "English") return T(0, key, args);
            if (g == "Both") return T(1, key, args) + "\n" + T(0, key, args);
            return T(1, key, args);
        }

        // ------------------------------------------------------------------
        // delivery
        // ------------------------------------------------------------------
        private const string HelloRpc = "j1ga.extendedbosses.hello";
        private const string AnnounceRpc = "j1ga.extendedbosses.announce";
        private const string MessageRpc = "j1ga.extendedbosses.msg";

        private readonly HashSet<long> _moddedPeers = new HashSet<long>();
        private bool _helloSent;

        private void RegisterTextRpcs()
        {
            ZRoutedRpc.instance.Register<ZPackage>(HelloRpc, new Action<long, ZPackage>(OnHello));
            ZRoutedRpc.instance.Register<ZPackage>(AnnounceRpc, new Action<long, ZPackage>(OnAnnounceRequest));
            ZRoutedRpc.instance.Register<ZPackage>(MessageRpc, new Action<long, ZPackage>(OnMessage));
        }

        // client -> server, once per connection
        private void TickHello()
        {
            if (_helloSent) return;
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer() || ZRoutedRpc.instance == null || Player.m_localPlayer == null) return;
            _helloSent = true;
            ZPackage pkg = new ZPackage();
            pkg.Write(Version);
            ZRoutedRpc.instance.InvokeRoutedRPC(HelloRpc, new object[] { pkg });   // no target = the server
        }

        private void OnHello(long sender, ZPackage pkg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            if (_moddedPeers.Add(sender)) Debug("peer " + sender + " has the mod (" + (pkg != null ? pkg.ReadString() : "?") + ")");
        }

        private static ZPackage TextPacket(string key, string[] args)
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(key);
            pkg.Write(args != null ? args.Length : 0);
            if (args != null) for (int i = 0; i < args.Length; i++) pkg.Write(args[i] ?? "");
            return pkg;
        }

        private static void ReadTextPacket(ZPackage pkg, out string key, out string[] args)
        {
            key = pkg.ReadString();
            int n = Mathf.Clamp(pkg.ReadInt(), 0, 16);
            args = new string[n];
            for (int i = 0; i < n; i++) args[i] = pkg.ReadString();
        }

        // Announce to everybody near and far; called by the boss owner.
        internal void Announce(string key, params string[] args)
        {
            string mode = Ss(_cfgAnnounce);
            if (mode == "Off" || string.IsNullOrEmpty(key) || ZRoutedRpc.instance == null) return;
            if (mode == "Chat")
            {
                if (Chat.instance != null) Chat.instance.SendText(Talker.Type.Normal, GuestText(key, args));
                return;
            }
            ZNet znet = ZNet.instance;
            if (znet != null && znet.IsServer()) { FanOut(key, args); return; }
            if (_serverSynced) ZRoutedRpc.instance.InvokeRoutedRPC(AnnounceRpc, new object[] { TextPacket(key, args) });
            else    // the server has no mod: nobody can sort the players - everyone gets the guest text
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", new object[] { (int)MessageHud.MessageType.Center, GuestText(key, args) });
        }

        private void OnAnnounceRequest(long sender, ZPackage pkg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || !znet.IsServer() || pkg == null) return;
                string key; string[] args;
                ReadTextPacket(pkg, out key, out args);
                FanOut(key, args);
            }
            catch (Exception e) { Fail("announce request", e); }
        }

        // server: our RPC to peers with the mod, vanilla ShowMessage to the rest, and ourselves
        private void FanOut(string key, string[] args)
        {
            ZNet znet = ZNet.instance;
            string guest = null;
            List<ZNetPeer> peers = znet.GetPeers();
            for (int i = 0; i < peers.Count; i++)
            {
                ZNetPeer peer = peers[i];
                if (peer == null || peer.m_uid == 0L) continue;
                if (_moddedPeers.Contains(peer.m_uid))
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, MessageRpc, new object[] { TextPacket(key, args) });
                else
                {
                    if (guest == null) guest = GuestText(key, args);
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", new object[] { (int)MessageHud.MessageType.Center, guest });
                }
            }
            if (!znet.IsDedicated()) ShowLocal(key, args);
        }

        private void OnMessage(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string key; string[] args;
                ReadTextPacket(pkg, out key, out args);
                ShowLocal(key, args);
            }
            catch (Exception e) { Fail("message", e); }
        }

        private void ShowLocal(string key, string[] args)
        {
            MessageHud hud = MessageHud.instance;
            if (hud == null) return;
            string text = L(key, args);
            hud.ShowMessage(MessageHud.MessageType.Center, text);
            float mul = _cfgMessageDuration.Value;
            if (mul > 1.01f || mul < 0.99f) StartCoroutine(StretchCenterMessage(hud, hud.m_messageCenterText.text, mul));
        }

        // Vanilla fades a centre message out over 4 s (a constant in MessageHud.ShowMessage),
        // applied in its next update. One frame later the fade is restarted with our length -
        // only for our message, only on clients with the mod.
        private const float VanillaCenterFade = 4f;

        private System.Collections.IEnumerator StretchCenterMessage(MessageHud hud, string text, float mul)
        {
            yield return null;
            yield return null;
            if (hud == null || hud.m_messageCenterText == null || hud.m_messageCenterText.text != text) yield break;
            hud.m_messageCenterText.CrossFadeAlpha(1f, 0f, true);
            hud.m_messageCenterText.CrossFadeAlpha(0f, VanillaCenterFade * mul, true);
        }

        // Boss name as a vanilla token, e.g. "$enemy_gdking" (resolved by each receiver).
        internal static string NameToken(Character c)
        {
            return c != null && !string.IsNullOrEmpty(c.m_name) ? c.m_name : "?";
        }

        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        private static class ZNet_Disconnect_Patch
        {
            private static void Postfix(ZNetPeer peer)
            {
                ExtendedBossesPlugin p = Instance;
                if (p == null || peer == null) return;
                p._moddedPeers.Remove(peer.m_uid);
            }
        }
    }
}
