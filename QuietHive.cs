using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace QuietHive
{
    [StaticConstructorOnStartup]
    public static class QuietHiveBootstrap
    {
        static QuietHiveBootstrap()
        {
            new Harmony("openai.sahar.quiethive").PatchAll();
        }
    }

    [DefOf]
    public static class QuietHiveDefOf
    {
        public static HediffDef QuietHive_Infection;
        public static HediffDef QuietHive_Exposed;
        public static JobDef QuietHive_CovertInfect;
        public static JobDef QuietHive_Lure;
        public static JobDef QuietHive_LuredFollow;
        public static JobDef QuietHive_Cover;
        public static ThingDef QuietHive_ParasiteMote;
        public static ThingDef QuietHive_JuvenileParasite;
        public static JobDef QuietHive_SeedParasite;
        public static JobDef QuietHive_ParasiteSeekHost;
        public static JobDef QuietHive_DoctorDeception;
        public static JobDef QuietHive_AmbushAssist;
        public static JobDef QuietHive_HideInBed;
        public static JobDef QuietHive_RetrieveParasite;
        public static ThingDef QuietHive_Evidence;
        public static ThingDef QuietHive_AdultParasite;
        public static ThingDef QuietHive_ContainmentPod;
        public static JobDef QuietHive_DestroyEvidence;
        public static JobDef QuietHive_RescueExposed;
        public static JobDef QuietHive_PlantHiddenParasite;
        public static ThingDef QuietHive_HiddenParasite;
        public static JobDef QuietHive_SabotageTreatment;
        public static ThingDef QuietHive_ShedSkin;
        public static ThingDef QuietHive_SlimeEvidence;
        public static ThingDef QuietHive_DeadJuvenile;
    }

    public class Hediff_QuietHiveInfection : HediffWithComps
    {
        public HediffComp_QuietHiveMind Mind => this.TryGetComp<HediffComp_QuietHiveMind>();
        public override bool Visible => pawn?.health?.hediffSet?.HasHediff(QuietHiveDefOf.QuietHive_Exposed) == true;
    }

    public class HediffCompProperties_QuietHiveMind : HediffCompProperties
    {
        public HediffCompProperties_QuietHiveMind() { compClass = typeof(HediffComp_QuietHiveMind); }
    }

    public class HediffComp_QuietHiveMind : HediffComp
    {
        public float suspicion;
        public int nextAttemptTick;
        public int infectionsCaused;
        public int closeCalls;
        public int lastWitnessedTick = -999999;
        public int infectedAtTick = -1;
        public int broodCount;
        public int nextBroodTick;
        public string hiveRole = "Generalist";
        public float learnedCaution;
        public int resistedCommands;
        public int lastLucidTick = -999999;
        public bool rememberedInfection;
        public string rememberedSafeRoom = "";
        public string rememberedDangerPawn = "";

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref suspicion, "quietHiveSuspicion", 0f);
            Scribe_Values.Look(ref nextAttemptTick, "quietHiveNextAttemptTick", 0);
            Scribe_Values.Look(ref infectionsCaused, "quietHiveInfectionsCaused", 0);
            Scribe_Values.Look(ref closeCalls, "quietHiveCloseCalls", 0);
            Scribe_Values.Look(ref lastWitnessedTick, "quietHiveLastWitnessedTick", -999999);
            Scribe_Values.Look(ref infectedAtTick, "quietHiveInfectedAtTick", -1);
            Scribe_Values.Look(ref broodCount, "quietHiveBroodCount", 0);
            Scribe_Values.Look(ref nextBroodTick, "quietHiveNextBroodTick", 0);
            Scribe_Values.Look(ref hiveRole, "quietHiveRole", "Generalist");
            Scribe_Values.Look(ref learnedCaution, "quietHiveLearnedCaution", 0f);
            Scribe_Values.Look(ref resistedCommands, "quietHiveResistedCommands", 0);
            Scribe_Values.Look(ref lastLucidTick, "quietHiveLastLucidTick", -999999);
            Scribe_Values.Look(ref rememberedInfection, "quietHiveRememberedInfection", false);
            Scribe_Values.Look(ref rememberedSafeRoom, "quietHiveRememberedSafeRoom", "");
            Scribe_Values.Look(ref rememberedDangerPawn, "quietHiveRememberedDangerPawn", "");
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            if (infectedAtTick < 0) infectedAtTick = Find.TickManager?.TicksGame ?? 0;
            if (nextBroodTick <= 0) nextBroodTick = (Find.TickManager?.TicksGame ?? 0) + Rand.RangeInclusive(45000, 90000);
            AssignRole();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (infectedAtTick < 0) infectedAtTick = Find.TickManager.TicksGame;
            if (suspicion > 0f && Find.TickManager.TicksGame % 600 == 0)
                suspicion = Math.Max(0f, suspicion - 0.012f);

            int now = Find.TickManager.TicksGame;
            if (now >= nextBroodTick && broodCount < (InfectionDays >= 8f ? 4 : 2))
            {
                broodCount++;
                // Established brood hosts replenish faster.
                int min = InfectionDays >= 8f ? 24000 : 45000;
                int max = InfectionDays >= 8f ? 48000 : 90000;
                nextBroodTick = now + Rand.RangeInclusive(min, max);
            }

            // Early infection occasionally produces a lucid/confused moment. These are deliberately
            // rare and fade as control becomes established.
            if (InfectionDays < 2.5f && now - lastLucidTick > 9000 && Rand.Chance(0.00045f))
            {
                lastLucidTick = now;
                rememberedInfection = rememberedInfection || Rand.Chance(0.45f);

                if (Pawn?.Faction == Faction.OfPlayer)
                {
                    string msg = rememberedInfection
                        ? Pawn.LabelShortCap + " has a disturbing flash of memory involving something crawling near their face."
                        : Pawn.LabelShortCap + " stops for a moment, visibly confused.";
                    Find.Message(msg, MessageTypeDefOf.NeutralEvent, false);
                }
            }
        }


        private void AssignRole()
        {
            if (Pawn == null || hiveRole != "Generalist") return;

            int medicine = Pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            int melee = Pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0;
            int social = Pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;

            if (medicine >= 8) hiveRole = "Doctor";
            else if (InfectionDays >= 8f) hiveRole = "Brood";
            else if (melee >= 8) hiveRole = "Protector";
            else if (social >= 8) hiveRole = "Hunter";
            else hiveRole = "Generalist";
        }

        public float InfectionDays
        {
            get
            {
                if (infectedAtTick < 0) return 0f;
                return Math.Max(0f, (Find.TickManager.TicksGame - infectedAtTick) / 60000f);
            }
        }
    }

    public class GameComponent_QuietHive : GameComponent
    {
        public float investigationProgress;
        public int confirmedCases;
        public int testsPerformed;
        public int treatmentsAttempted;
        public float hiveIntelligence;
        public int evidenceFound;
        public int sabotagedTests;
        public bool hiveEstablished;
        public bool playerEmbracedHive;
        public int adultParasitesExtracted;
        public float parasiteResearch;
        public int outsideInfections;
        public int factionsSeeded;
        public Dictionary<string, string> sharedPawnKnowledge = new Dictionary<string, string>();
        public Dictionary<string, int> sharedRoomKnowledge = new Dictionary<string, int>();

        // Hidden parasite allegiance. This deliberately does NOT replace Pawn.Faction:
        // outsiders continue to appear as members of their original faction to the world.
        public Dictionary<string, string> parasiteFactionMembers = new Dictionary<string, string>();
        public int hiveCycleIndex;

        public GameComponent_QuietHive(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref investigationProgress, "quietHiveInvestigationProgress", 0f);
            Scribe_Values.Look(ref confirmedCases, "quietHiveConfirmedCases", 0);
            Scribe_Values.Look(ref testsPerformed, "quietHiveTestsPerformed", 0);
            Scribe_Values.Look(ref treatmentsAttempted, "quietHiveTreatmentsAttempted", 0);
            Scribe_Values.Look(ref hiveIntelligence, "quietHiveIntelligence", 0f);
            Scribe_Values.Look(ref evidenceFound, "quietHiveEvidenceFound", 0);
            Scribe_Values.Look(ref sabotagedTests, "quietHiveSabotagedTests", 0);
            Scribe_Values.Look(ref hiveEstablished, "quietHiveEstablished", false);
            Scribe_Values.Look(ref playerEmbracedHive, "quietHivePlayerEmbracedHive", false);
            Scribe_Values.Look(ref adultParasitesExtracted, "quietHiveAdultParasitesExtracted", 0);
            Scribe_Values.Look(ref parasiteResearch, "quietHiveParasiteResearch", 0f);
            Scribe_Values.Look(ref outsideInfections, "quietHiveOutsideInfections", 0);
            Scribe_Values.Look(ref factionsSeeded, "quietHiveFactionsSeeded", 0);
            Scribe_Collections.Look(ref sharedPawnKnowledge, "quietHiveSharedPawnKnowledge", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref sharedRoomKnowledge, "quietHiveSharedRoomKnowledge", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref parasiteFactionMembers, "quietHiveParasiteFactionMembers", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref hiveCycleIndex, "quietHiveCycleIndex", 0);
            if (sharedPawnKnowledge == null) sharedPawnKnowledge = new Dictionary<string, string>();
            if (sharedRoomKnowledge == null) sharedRoomKnowledge = new Dictionary<string, int>();
            if (parasiteFactionMembers == null) parasiteFactionMembers = new Dictionary<string, string>();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 0) return;

            foreach (Map map in Find.Maps)
            {
                List<Pawn> pawns = map.mapPawns.AllPawnsSpawned.ToList();
                int hiveSize = pawns.Count(IsInfected);

                foreach (Pawn infectedPawn in pawns.Where(IsInfected))
                    RegisterParasiteFactionMember(infectedPawn);

                List<Pawn> playerHumans = map.mapPawns.FreeColonistsSpawned.Where(p => p.RaceProps.Humanlike && !p.Dead).ToList();
                if (playerHumans.Count > 0 && playerHumans.All(IsInfected) && !hiveEstablished)
                {
                    hiveEstablished = true;
                    Find.LetterStack.ReceiveLetter(
                        "Hive Established",
                        "Every humanlike colonist on this map is infected. Internal secrecy is no longer necessary; the hive will increasingly focus on outsiders, prisoners, visitors, and preserving itself.",
                        LetterDefOf.ThreatSmall);
                }
                if (playerEmbracedHive && outsideInfections == 10 && now % 250 == 0)
                {
                    Find.LetterStack.ReceiveLetter(
                        "The Hive Spreads",
                        "Ten outsiders have been successfully infected. The colony is no longer merely a local hive; it is beginning to seed the wider world.",
                        LetterDefOf.PositiveEvent);
                    outsideInfections++; // prevent repeat at exact threshold
                }

                if (!playerEmbracedHive && parasiteResearch >= 0.95f && investigationProgress >= 0.95f && now % 250 == 0)
                {
                    // Knowledge victory hook: the player has effectively mastered detection/removal.
                    parasiteResearch = 0.951f; // stable marker; UI reflects mastery without repeated letter spam below
                }

                foreach (Pawn host in pawns)
                {
                    if (!CanHostAct(host)) continue;
                    HediffComp_QuietHiveMind mind = GetMind(host);
                    if (mind == null || now < mind.nextAttemptTick) continue;
                    if (mind.broodCount <= 0) continue;

                    if (mind.suspicion >= 0.65f || now - mind.lastWitnessedTick < 4000)
                    {
                        mind.nextAttemptTick = now + Rand.RangeInclusive(5000, 9000);
                        continue;
                    }

                    // Protectors and doctors actively clean up physical evidence or rescue exposed hosts.
                    Thing evidence = map.listerThings.ThingsOfDef(QuietHiveDefOf.QuietHive_Evidence)
                        .Where(t => t.Spawned && host.CanReach(t, PathEndMode.Touch, Danger.Some))
                        .OrderBy(t => host.Position.DistanceTo(t.Position))
                        .FirstOrDefault();

                    if (evidence != null && (mind.hiveRole == "Protector" || mind.hiveRole == "Doctor") && Rand.Chance(0.30f))
                    {
                        host.jobs.StartJob(JobMaker.MakeJob(QuietHiveDefOf.QuietHive_DestroyEvidence, evidence), JobCondition.InterruptOptional);
                        mind.nextAttemptTick = now + Rand.RangeInclusive(1600, 3000);
                        continue;
                    }

                    Pawn exposedAlly = pawns.Where(p => p != host && IsInfected(p) &&
                        p.health?.hediffSet?.HasHediff(QuietHiveDefOf.QuietHive_Exposed) == true &&
                        !p.Dead && p.Spawned && host.CanReach(p, PathEndMode.Touch, Danger.Some))
                        .OrderBy(p => host.Position.DistanceTo(p.Position))
                        .FirstOrDefault();

                    if (exposedAlly != null && mind.hiveRole == "Protector" && Rand.Chance(0.14f))
                    {
                        host.jobs.StartJob(JobMaker.MakeJob(QuietHiveDefOf.QuietHive_RescueExposed, exposedAlly), JobCondition.InterruptOptional);
                        mind.nextAttemptTick = now + Rand.RangeInclusive(3000, 6000);
                        continue;
                    }

                    // Recover loose juveniles before they become evidence, especially for Brood/Protector roles.
                    Thing loose = map.listerThings.ThingsOfDef(QuietHiveDefOf.QuietHive_JuvenileParasite)
                        .Where(t => t.Spawned && host.CanReach(t, PathEndMode.Touch, Danger.Some))
                        .OrderBy(t => host.Position.DistanceTo(t.Position))
                        .FirstOrDefault();

                    if (loose != null && (mind.hiveRole == "Brood" || mind.hiveRole == "Protector") && Rand.Chance(0.28f))
                    {
                        host.jobs.StartJob(JobMaker.MakeJob(QuietHiveDefOf.QuietHive_RetrieveParasite, loose), JobCondition.InterruptOptional);
                        mind.nextAttemptTick = now + Rand.RangeInclusive(1800, 3200);
                        continue;
                    }

                    // Hunters/Brood hosts sometimes hide a juvenile near a bed instead of making a direct attempt.
                    if (mind.broodCount > 0 && (mind.hiveRole == "Hunter" || mind.hiveRole == "Brood") && Rand.Chance(0.12f))
                    {
                        Building_Bed bed = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                            .Where(b => b.Spawned && host.CanReach(b.Position, PathEndMode.OnCell, Danger.Some))
                            .OrderBy(b => host.Position.DistanceTo(b.Position))
                            .FirstOrDefault();
                        if (bed != null)
                        {
                            host.jobs.StartJob(JobMaker.MakeJob(QuietHiveDefOf.QuietHive_HideInBed, bed.Position), JobCondition.InterruptOptional);
                            mind.nextAttemptTick = now + Rand.RangeInclusive(3500, 6500);
                            continue;
                        }
                    }

                    Pawn target = PickTarget(host, map, hiveSize, out int witnesses, out float score);
                    if (target == null)
                    {
                        mind.nextAttemptTick = now + Rand.RangeInclusive(1200, 2400);
                        continue;
                    }

                    // An infected doctor can exploit legitimate access to a patient who is
                    // already lying down. This does not make the parasite invisible; the doctor
                    // still waits for the actual transfer to be unwitnessed.
                    int medicine = host.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
                    bool patientOpportunity = target.CurJobDef == JobDefOf.LayDown || target.Downed;
                    if (medicine >= 6 && patientOpportunity && Rand.Chance(0.34f))
                    {
                        Job doctorJob = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_DoctorDeception, target);
                        host.jobs.StartJob(doctorJob, JobCondition.InterruptOptional);
                        mind.nextAttemptTick = now + Rand.RangeInclusive(3000, 5200);
                        continue;
                    }

                    if (witnesses > 0)
                    {
                        if (score >= 12f && Rand.Chance(0.55f))
                        {
                            IntVec3 privateCell = FindPrivateLureCell(host, target);
                            if (privateCell.IsValid)
                            {
                                Job lure = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_Lure, target, privateCell);
                                host.jobs.StartJob(lure, JobCondition.InterruptOptional);
                            }
                        }
                        mind.nextAttemptTick = now + Rand.RangeInclusive(1000, 2200);
                        continue;
                    }

                    StartCovertInfection(host, target, now);
                }
            }
        }

        private static void StartCovertInfection(Pawn host, Pawn target, int now)
        {
            HediffComp_QuietHiveMind hostMind = GetMind(host);
            if (hostMind != null && hostMind.InfectionDays < 1.5f)
            {
                float resistChance = Mathf.Lerp(0.34f, 0.08f, hostMind.InfectionDays / 1.5f);
                if (Rand.Chance(resistChance))
                {
                    hostMind.resistedCommands++;
                    hostMind.nextAttemptTick = now + Rand.RangeInclusive(2200, 4200);
                    if (host.Faction == Faction.OfPlayer && Rand.Chance(0.28f))
                        Find.Message(host.LabelShortCap + " pauses, confused, and abandons what they were about to do.", MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
            }

            Pawn helper = null;

            // Alert victims can trigger a coordinated two-host ambush. The helper moves into
            // touching range and distracts/restrains while the primary host performs the transfer.
            if (target.Awake() && !target.Downed && Rand.Chance(0.42f))
            {
                helper = FindCoverPawn(host, target);
                if (helper != null && CanHostAct(helper))
                {
                    Job assist = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_AmbushAssist, target);
                    helper.jobs.StartJob(assist, JobCondition.InterruptOptional);
                    HediffComp_QuietHiveMind hm = GetMind(helper);
                    if (hm != null) hm.nextAttemptTick = now + Rand.RangeInclusive(2800, 4800);
                }
            }

            Job job = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_CovertInfect, target);
            host.jobs.StartJob(job, JobCondition.InterruptOptional);
            HediffComp_QuietHiveMind mind = GetMind(host);
            if (mind != null) mind.nextAttemptTick = now + Rand.RangeInclusive(3500, 6500);

            if (helper == null)
            {
                Pawn cover = FindCoverPawn(host, target);
                if (cover != null && CanHostAct(cover) && Rand.Chance(0.35f))
                {
                    Job coverJob = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_Cover, host);
                    cover.jobs.StartJob(coverJob, JobCondition.InterruptOptional);
                    HediffComp_QuietHiveMind coverMind = GetMind(cover);
                    if (coverMind != null) coverMind.nextAttemptTick = now + Rand.RangeInclusive(1800, 3200);
                }
            }
        }

        public static int AmbushAssistantsNear(Pawn victim)
        {
            if (victim?.Map == null) return 0;
            return victim.Map.mapPawns.AllPawnsSpawned.Count(p =>
                p != victim && IsInfected(p) && !p.Dead && !p.Downed &&
                p.CurJobDef == QuietHiveDefOf.QuietHive_AmbushAssist &&
                p.Position.DistanceTo(victim.Position) <= 2.4f);
        }

        private static bool CanHostAct(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Drafted) return false;
            if (pawn.InMentalState || pawn.jobs == null || pawn.Map == null || !IsInfected(pawn)) return false;
            Job cur = pawn.CurJob;
            return cur == null || (cur.def != JobDefOf.AttackMelee && cur.def != JobDefOf.AttackStatic &&
                cur.def != QuietHiveDefOf.QuietHive_CovertInfect && cur.def != QuietHiveDefOf.QuietHive_Lure &&
                cur.def != QuietHiveDefOf.QuietHive_LuredFollow && cur.def != QuietHiveDefOf.QuietHive_Cover &&
                cur.def != QuietHiveDefOf.QuietHive_DoctorDeception && cur.def != QuietHiveDefOf.QuietHive_AmbushAssist);
        }

        public static bool IsInfected(Pawn pawn) => pawn?.health?.hediffSet?.HasHediff(QuietHiveDefOf.QuietHive_Infection) == true;

        public static HediffComp_QuietHiveMind GetMind(Pawn pawn)
        {
            Hediff_QuietHiveInfection h = pawn?.health?.hediffSet?.GetFirstHediffOfDef(QuietHiveDefOf.QuietHive_Infection) as Hediff_QuietHiveInfection;
            return h?.Mind;
        }

        private static Pawn PickTarget(Pawn host, Map map, int hiveSize, out int bestWitnesses, out float bestScore)
        {
            Pawn best = null;
            bestScore = float.MinValue;
            bestWitnesses = 99;

            foreach (Pawn target in map.mapPawns.AllPawnsSpawned)
            {
                if (target == host || target.Dead || IsInfected(target) || !target.RaceProps.Humanlike) continue;
                if (target.HostileTo(host) || !host.CanReach(target, PathEndMode.Touch, Danger.Some)) continue;

                int witnesses = CountWitnesses(host, target);
                float distance = host.Position.DistanceTo(target.Position);
                float score = 30f - distance;

                if (target.Downed) score += 30f;
                if (!target.Awake()) score += 24f;
                if (target.CurJobDef == JobDefOf.LayDown) score += 18f;

                // Prisoners are deliberately prioritized: they are confined, easier to isolate,
                // and outsiders are less likely to question routine access to them.
                if (target.IsPrisoner) score += 22f;
                if (target.IsPrisonerOfColony) score += 8f;

                GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
                int targetMed = target.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
                if (target.equipment?.Primary != null) score -= 8f + gc.hiveIntelligence * 10f;
                if (targetMed >= 8) score -= 5f + gc.hiveIntelligence * 8f;
                if (!target.Awake()) score += gc.hiveIntelligence * 8f;

                HediffComp_QuietHiveMind hm = GetMind(host);
                if (hm != null)
                {
                    Room tr = target.Position.GetRoom(map);
                    if (tr != null && !tr.PsychologicallyOutdoors && CountWitnesses(host, target) == 0)
                        hm.rememberedSafeRoom = tr.ID.ToString();

                    if (target.equipment?.Primary != null || targetMed >= 10)
                        hm.rememberedDangerPawn = target.ThingID;

                    string facts = (target.equipment?.Primary != null ? "armed;" : "") +
                                   (targetMed >= 8 ? "doctor;" : "") +
                                   (!target.Awake() ? "sleeping;" : "") +
                                   (target.IsPrisoner ? "prisoner;" : "");
                    gc.sharedPawnKnowledge[target.ThingID] = facts;
                    if (tr != null && !tr.PsychologicallyOutdoors && CountWitnesses(host, target) == 0)
                        gc.sharedRoomKnowledge[tr.ID.ToString()] = Find.TickManager.TicksGame;
                }
                Room room = target.Position.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors) score += 5f;

                int nearbyNormals = map.mapPawns.AllPawnsSpawned.Count(p => p != host && p != target && !p.Downed && !IsInfected(p) && p.Position.DistanceTo(target.Position) <= 8f);
                score -= nearbyNormals * 5f;
                score -= witnesses * 28f;
                score += Math.Min(6f, hiveSize * 0.5f);

                if (score > bestScore)
                {
                    best = target;
                    bestScore = score;
                    bestWitnesses = witnesses;
                }
            }
            return bestScore >= 8f ? best : null;
        }

        public static int CountWitnesses(Pawn host, Pawn target)
        {
            if (host?.Map == null || target?.Map != host.Map) return 99;
            int count = 0;
            foreach (Pawn witness in host.Map.mapPawns.AllPawnsSpawned)
            {
                if (witness == host || witness == target || witness.Dead || witness.Downed || IsInfected(witness)) continue;
                if (!witness.RaceProps.Humanlike || !witness.Awake()) continue;
                if (witness.Position.DistanceTo(target.Position) > 16f) continue;
                if (GenSight.LineOfSight(witness.Position, target.Position, host.Map)) count++;
            }
            return count;
        }

        public static void Witnessed(Pawn host, Pawn victim, int witnessCount)
        {
            HediffComp_QuietHiveMind mind = GetMind(host);
            if (mind != null)
            {
                mind.closeCalls++;
                mind.lastWitnessedTick = Find.TickManager.TicksGame;
                mind.suspicion = Math.Min(1f, mind.suspicion + 0.22f + 0.12f * Math.Max(0, witnessCount - 1));
                mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(5000, 9000);
            }

            ExposeHost(host, victim, "was caught doing something disturbing to");
        }

        public static void ExposeHost(Pawn host, Pawn contextPawn, string reason)
        {
            if (host?.health == null || host.health.hediffSet.HasHediff(QuietHiveDefOf.QuietHive_Exposed)) return;
            host.health.AddHediff(QuietHiveDefOf.QuietHive_Exposed);
            Current.Game.GetComponent<GameComponent_QuietHive>().investigationProgress = Math.Min(1f,
                Current.Game.GetComponent<GameComponent_QuietHive>().investigationProgress + 0.18f);
            if (host.Faction == Faction.OfPlayer)
            {
                string context = contextPawn == null ? "" : " " + reason + " " + contextPawn.LabelShort + ".";
                Find.LetterStack.ReceiveLetter("Suspicious parasitic signs", host.LabelShortCap + context + " Medical testing can now investigate the possibility of a hidden parasite.", LetterDefOf.ThreatSmall, host);
            }
        }

        public static IntVec3 FindPrivateLureCell(Pawn host, Pawn victim)
        {
            if (host?.Map == null) return IntVec3.Invalid;
            Map map = host.Map;
            IntVec3 best = IntVec3.Invalid;
            float bestScore = float.MinValue;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(victim.Position, 18f, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map) || !host.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors) continue;

                int normals = map.mapPawns.AllPawnsSpawned.Count(p => !p.Dead && !p.Downed && !IsInfected(p) && p != victim && p.Position.DistanceTo(cell) <= 9f);
                float distance = victim.Position.DistanceTo(cell);
                float score = 22f - distance - normals * 8f;
                if (room.CellCount <= 45) score += 7f;
                if (normals == 0) score += 12f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return bestScore >= 8f ? best : IntVec3.Invalid;
        }

        private static Pawn FindCoverPawn(Pawn host, Pawn target)
        {
            if (host?.Map == null) return null;
            return host.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != host && p != target && IsInfected(p) && !p.Dead && !p.Downed && p.Position.DistanceTo(target.Position) <= 18f)
                .OrderBy(p => p.Position.DistanceTo(target.Position))
                .FirstOrDefault();
        }




        public static void OnPawnInfected(Pawn newHost, Pawn sourceHost = null)
        {
            if (newHost == null || Current.Game == null) return;

            RegisterParasiteFactionMember(newHost);

            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();

            if (gc.playerEmbracedHive)
            {
                if (sourceHost != null && IsParasiteFactionMember(sourceHost))
                {
                    HediffComp_QuietHiveMind childMind = GetMind(newHost);
                    HediffComp_QuietHiveMind sourceMind = GetMind(sourceHost);

                    if (childMind != null && sourceMind != null)
                    {
                        childMind.learnedCaution = Mathf.Max(childMind.learnedCaution, sourceMind.learnedCaution * 0.5f);
                        childMind.rememberedSafeRoom = sourceMind.rememberedSafeRoom;
                        childMind.rememberedDangerPawn = sourceMind.rememberedDangerPawn;
                    }
                }

                if (newHost.Spawned && newHost.Map != null)
                    MoteMaker.ThrowText(newHost.DrawPos, newHost.Map, "joined hive", 0.55f);
            }
        }

        public static void RegisterParasiteFactionMember(Pawn pawn)
        {
            if (pawn == null || !IsInfected(pawn) || Current.Game == null) return;
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            string outwardFaction = pawn.Faction?.def?.defName ?? "NoFaction";
            gc.parasiteFactionMembers[pawn.ThingID] = outwardFaction;
        }

        public static bool IsParasiteFactionMember(Pawn pawn)
        {
            if (pawn == null || Current.Game == null || !IsInfected(pawn)) return false;
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            RegisterParasiteFactionMember(pawn);
            return gc.parasiteFactionMembers.ContainsKey(pawn.ThingID);
        }

        public static bool IsHiveControlled(Pawn pawn)
        {
            if (pawn == null || Current.Game == null) return false;
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            return gc.playerEmbracedHive && IsParasiteFactionMember(pawn);
        }

        public static List<Pawn> LoadedHiveMembers()
        {
            if (Current.Game == null) return new List<Pawn>();
            return Find.Maps
                .SelectMany(m => m.mapPawns.AllPawnsSpawned)
                .Where(p => p.RaceProps.Humanlike && IsParasiteFactionMember(p) && !p.Dead)
                .OrderBy(p => p.Map?.uniqueID ?? 0)
                .ThenBy(p => p.LabelShort)
                .ToList();
        }

        public static string HiveNetworkReport()
        {
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            List<Pawn> members = LoadedHiveMembers();

            if (members.Count == 0)
                return "No currently loaded living hive members.";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("QUIET HIVE NETWORK");
            sb.AppendLine("Loaded members: " + members.Count);
            sb.AppendLine("Outside infections: " + gc.outsideInfections);
            sb.AppendLine();

            foreach (Pawn p in members)
            {
                HediffComp_QuietHiveMind mind = GetMind(p);
                string outward = gc.parasiteFactionMembers.TryGetValue(p.ThingID, out string f) ? f : (p.Faction?.Name ?? "None");
                string mapName = p.Map?.Parent?.LabelCap ?? p.Map?.ToString() ?? "unknown map";
                sb.Append("• ").Append(p.LabelShortCap)
                  .Append(" | outward: ").Append(outward)
                  .Append(" | role: ").Append(mind?.hiveRole ?? "Unknown")
                  .Append(" | brood: ").Append(mind?.broodCount ?? 0)
                  .Append(" | control: ").Append(gc.playerEmbracedHive ? "YES" : "hidden")
                  .Append(" | ").Append(mapName)
                  .AppendLine();
            }
            return sb.ToString();
        }

        public static void CycleToNextHiveMember()
        {
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            List<Pawn> members = LoadedHiveMembers();
            if (members.Count == 0)
            {
                Find.Message("No loaded hive members.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            gc.hiveCycleIndex %= members.Count;
            Pawn next = members[gc.hiveCycleIndex];
            gc.hiveCycleIndex = (gc.hiveCycleIndex + 1) % members.Count;

            if (next.Map != null)
            {
                Current.Game.CurrentMap = next.Map;
                Find.CameraDriver.JumpToCurrentMapLoc(next.Position);
                Find.Selector.ClearSelection();
                Find.Selector.Select(next);
            }
        }


        public static IEnumerable<Pawn> ValidHiveCommandTargets(Pawn agent)
        {
            if (agent?.Map == null) yield break;

            foreach (Pawn p in agent.Map.mapPawns.AllPawnsSpawned)
            {
                if (p == agent || p.Dead || !p.RaceProps.Humanlike) continue;
                if (IsInfected(p)) continue;
                if (!agent.CanReach(p, PathEndMode.Touch, Danger.Some)) continue;
                yield return p;
            }
        }

        public static void OrderHiveInfect(Pawn agent, Pawn target)
        {
            if (!IsHiveControlled(agent) || target == null || target.Dead || IsInfected(target)) return;

            HediffComp_QuietHiveMind mind = GetMind(agent);
            if (mind == null || mind.broodCount <= 0)
            {
                Find.Message(agent.LabelShortCap + " has no mature juvenile ready.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Job job = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_CovertInfect, target);
            agent.jobs.StartJob(job, JobCondition.InterruptForced);
            mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(2500, 4200);
        }

        public static void OrderHiveIsolate(Pawn agent, Pawn target)
        {
            if (!IsHiveControlled(agent) || target == null || target.Dead || IsInfected(target)) return;

            IntVec3 cell = FindPrivateLureCell(agent, target);
            if (!cell.IsValid)
            {
                Find.Message("No suitable private location was found.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Job job = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_Lure, target, cell);
            agent.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public static void OrderHiveAmbushAssist(Pawn agent, Pawn target)
        {
            if (!IsHiveControlled(agent) || target == null || target.Dead || IsInfected(target)) return;
            Job assist = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_AmbushAssist, target);
            agent.jobs.StartJob(assist, JobCondition.InterruptForced);
        }


        public static void OrderPlantHiddenParasite(Pawn agent, IntVec3 cell)
        {
            if (!IsHiveControlled(agent) || agent?.Map == null) return;

            HediffComp_QuietHiveMind mind = GetMind(agent);
            if (mind == null || mind.broodCount <= 0)
            {
                Find.Message(agent.LabelShortCap + " has no mature juvenile ready.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!cell.IsValid || !cell.InBounds(agent.Map) || !cell.Standable(agent.Map) ||
                !agent.CanReach(cell, PathEndMode.OnCell, Danger.Some))
            {
                Find.Message("Choose a reachable floor or bed cell.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Job plant = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_PlantHiddenParasite, cell);
            agent.jobs.StartJob(plant, JobCondition.InterruptForced);
        }

        public static void SpawnEvidence(IntVec3 cell, Map map, string reason)
        {
            if (map == null || !cell.InBounds(map) || !Rand.Chance(0.42f)) return;

            ThingDef def = QuietHiveDefOf.QuietHive_Evidence;
            float roll = Rand.Value;
            if (roll < 0.28f && QuietHiveDefOf.QuietHive_ShedSkin != null) def = QuietHiveDefOf.QuietHive_ShedSkin;
            else if (roll < 0.56f && QuietHiveDefOf.QuietHive_SlimeEvidence != null) def = QuietHiveDefOf.QuietHive_SlimeEvidence;
            else if (roll < 0.68f && QuietHiveDefOf.QuietHive_DeadJuvenile != null) def = QuietHiveDefOf.QuietHive_DeadJuvenile;

            if (def != null)
            {
                Thing ev = ThingMaker.MakeThing(def);
                GenSpawn.Spawn(ev, cell, map);
            }
        }

        public static void StudySpecimen(Thing specimen)
        {
            if (specimen == null || specimen.Destroyed) return;
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            float gain = specimen.def == QuietHiveDefOf.QuietHive_AdultParasite ? 0.18f : 0.07f;
            gc.parasiteResearch = Mathf.Clamp01(gc.parasiteResearch + gain);
            gc.investigationProgress = Mathf.Clamp01(gc.investigationProgress + gain * 0.55f);
            Find.Message("Parasite research advanced to " + Mathf.RoundToInt(gc.parasiteResearch * 100f) + "%.", MessageTypeDefOf.PositiveEvent, false);
        }

        public static void RunTest(Pawn pawn)
        {
            GameComponent_QuietHive game = Current.Game.GetComponent<GameComponent_QuietHive>();
            game.testsPerformed++;
            bool infected = IsInfected(pawn);
            bool positive = false;

            Pawn doctor = BestDoctor(pawn.Map, pawn);
            bool doctorSabotage = doctor != null && IsInfected(doctor) &&
                (GetMind(doctor)?.hiveRole == "Doctor" || (doctor.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) >= 8) &&
                Rand.Chance(0.72f);

            if (doctorSabotage)
            {
                game.sabotagedTests++;
                HediffComp_QuietHiveMind dm = GetMind(doctor);
                if (dm != null)
                {
                    dm.suspicion = Math.Max(0f, dm.suspicion - 0.03f);
                    dm.learnedCaution = Mathf.Clamp01(dm.learnedCaution + 0.08f);
                }
                Find.Message("Quiet Hive test on " + pawn.LabelShortCap + ": no parasite detected.", MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            if (infected)
            {
                HediffComp_QuietHiveMind mind = GetMind(pawn);
                float days = mind?.InfectionDays ?? 0f;
                float sensitivity = Mathf.Clamp01(0.28f + days * 0.17f + game.investigationProgress * 0.35f + game.parasiteResearch * 0.22f);
                positive = Rand.Chance(sensitivity);
            }
            else
            {
                // Small false-positive chance keeps testing from being perfect information.
                positive = Rand.Chance(0.015f);
            }

            if (positive)
            {
                game.confirmedCases++;
                game.investigationProgress = Mathf.Clamp01(game.investigationProgress + 0.22f);
                if (infected && !pawn.health.hediffSet.HasHediff(QuietHiveDefOf.QuietHive_Exposed))
                    pawn.health.AddHediff(QuietHiveDefOf.QuietHive_Exposed);
                Find.LetterStack.ReceiveLetter("Quiet Hive test: POSITIVE", "Testing of " + pawn.LabelShortCap + " detected strong evidence of the parasite. Colony investigation accuracy has improved.", LetterDefOf.ThreatSmall, pawn);
            }
            else
            {
                Find.Message("Quiet Hive test on " + pawn.LabelShortCap + ": no parasite detected. Early infections can evade testing.", MessageTypeDefOf.NeutralEvent, false);
            }
        }

        public static void RunTreatment(Pawn pawn)
        {
            if (!IsInfected(pawn))
            {
                Find.Message(pawn.LabelShortCap + " has no confirmed Quiet Hive infection to treat.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            GameComponent_QuietHive game = Current.Game.GetComponent<GameComponent_QuietHive>();
            game.treatmentsAttempted++;
            Pawn doctor = BestDoctor(pawn.Map, pawn);
            float medicineSkill = doctor?.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 6f;
            float days = GetMind(pawn)?.InfectionDays ?? 0f;
            float chance = Mathf.Clamp01(0.68f + medicineSkill * 0.018f - days * 0.045f + game.parasiteResearch * 0.18f);

            bool sabotage = doctor != null && IsInfected(doctor) &&
                (GetMind(doctor)?.hiveRole == "Doctor") && Rand.Chance(0.60f);
            if (sabotage)
                chance *= 0.18f;

            if (Rand.Chance(chance))
            {
                Hediff infection = pawn.health.hediffSet.GetFirstHediffOfDef(QuietHiveDefOf.QuietHive_Infection);
                if (infection != null) pawn.health.RemoveHediff(infection);
                SpawnAdultParasite(pawn.Position, pawn.Map);
                Hediff exposed = pawn.health.hediffSet.GetFirstHediffOfDef(QuietHiveDefOf.QuietHive_Exposed);
                if (exposed != null) pawn.health.RemoveHediff(exposed);
                Find.LetterStack.ReceiveLetter("Parasite removed", "The parasite was successfully removed from " + pawn.LabelShortCap + ".", LetterDefOf.PositiveEvent, pawn);
            }
            else
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, Rand.Range(2f, 6f), 0f, -1f, doctor));
                Find.LetterStack.ReceiveLetter("Removal failed", "The attempt to remove the parasite from " + pawn.LabelShortCap + " failed and caused surgical injury. A later attempt may still succeed.", LetterDefOf.NegativeEvent, pawn);
            }
        }


        public static void ExamineCorpseForParasite(Pawn corpsePawn)
        {
            if (corpsePawn == null || !corpsePawn.Dead) return;
            bool infected = IsInfected(corpsePawn);

            if (!infected)
            {
                Find.Message("No Quiet Hive parasite was found.", MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            gc.investigationProgress = Mathf.Clamp01(gc.investigationProgress + 0.35f);
            gc.confirmedCases++;
            SpawnAdultParasite(corpsePawn.Position, corpsePawn.Map);
            Find.LetterStack.ReceiveLetter(
                "Parasite discovered in autopsy",
                "An adult parasite was found inside " + corpsePawn.LabelShortCap + ". This provides decisive evidence and greatly improves the colony's investigation.",
                LetterDefOf.ThreatSmall,
                corpsePawn);
        }

        public static void SpawnAdultParasite(IntVec3 cell, Map map)
        {
            if (map == null || QuietHiveDefOf.QuietHive_AdultParasite == null) return;
            Thing adult = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_AdultParasite);
            GenSpawn.Spawn(adult, cell, map);
            Current.Game.GetComponent<GameComponent_QuietHive>().adultParasitesExtracted++;
        }

        public static void EmbraceHive()
        {
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            gc.playerEmbracedHive = true;

            foreach (Map map in Find.Maps)
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(IsInfected))
                    RegisterParasiteFactionMember(p);

            Find.LetterStack.ReceiveLetter(
                "The colony embraces the hive",
                "You have chosen to cooperate with the parasite. Every loaded infected pawn is now secretly registered in the parasite faction while retaining their outward faction identity. Select infected outsiders to issue hive-control commands, or open the Hive Network to watch the entire infection.",
                LetterDefOf.PositiveEvent);
        }

        private static Pawn BestDoctor(Map map, Pawn patient)
        {
            if (map == null) return null;
            return map.mapPawns.FreeColonistsSpawned
                .Where(p => p != patient && !p.Dead && !p.Downed)
                .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0)
                .FirstOrDefault();
        }
    }


    // When the hive is embraced, infected pawns remain members of their outward faction but are
    // considered player-controllable by order checks. This is the core "secret parasite faction":
    // diplomatic/faction identity stays untouched while the hive link grants command authority.
    [HarmonyPatch(typeof(Pawn), "get_IsColonistPlayerControlled")]
    public static class Patch_Pawn_IsColonistPlayerControlled_QuietHive
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result && GameComponent_QuietHive.IsHiveControlled(__instance))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_Pawn_GetGizmos_QuietHive
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance == null || !__instance.RaceProps.Humanlike) return;

            GameComponent_QuietHive gc = Current.Game?.GetComponent<GameComponent_QuietHive>();
            bool normalPlayerPawn = __instance.Faction == Faction.OfPlayer;
            bool hivePawn = gc != null && gc.playerEmbracedHive && GameComponent_QuietHive.IsParasiteFactionMember(__instance);
            if (!normalPlayerPawn && !hivePawn) return;

            List<Gizmo> result = __result.ToList();

            if (gc != null && gc.playerEmbracedHive && GameComponent_QuietHive.IsParasiteFactionMember(__instance))
            {
                result.Add(new Command_Action
                {
                    defaultLabel = "Hive Network",
                    defaultDesc = "Show every currently loaded infected pawn, including infected outsiders who still appear to belong to their original factions.",
                    icon = TexCommand.Inspect,
                    action = () => Find.WindowStack.Add(new Dialog_Message(GameComponent_QuietHive.HiveNetworkReport()))
                });

                result.Add(new Command_Action
                {
                    defaultLabel = "Next hive member",
                    defaultDesc = "Jump the camera to and select the next infected pawn in the hidden parasite faction.",
                    icon = TexCommand.SelectNextTransporter,
                    action = () => GameComponent_QuietHive.CycleToNextHiveMember()
                });


                result.Add(new Command_Target
                {
                    defaultLabel = "Hive: Infect target",
                    defaultDesc = "Order this hive agent to infect a specific uninfected pawn using a mature juvenile. Awake victims may resist; witnesses can expose the attempt.",
                    icon = TexCommand.Attack,
                    targetingParams = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetSelf = false
                    },
                    action = delegate(LocalTargetInfo target)
                    {
                        Pawn victim = target.Pawn;
                        if (victim == null || victim.Dead || !victim.RaceProps.Humanlike || GameComponent_QuietHive.IsInfected(victim))
                        {
                            Find.Message("Choose an uninfected humanlike pawn.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        GameComponent_QuietHive.OrderHiveInfect(__instance, victim);
                    }
                });

                result.Add(new Command_Target
                {
                    defaultLabel = "Hive: Isolate target",
                    defaultDesc = "Order this hive agent to lure a specific pawn into a quiet indoor location. The agent will try to make the isolation look like ordinary social behavior.",
                    icon = TexCommand.GatherSpotActive,
                    targetingParams = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetSelf = false
                    },
                    action = delegate(LocalTargetInfo target)
                    {
                        Pawn victim = target.Pawn;
                        if (victim == null || victim.Dead || !victim.RaceProps.Humanlike || GameComponent_QuietHive.IsInfected(victim))
                        {
                            Find.Message("Choose an uninfected humanlike pawn.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        GameComponent_QuietHive.OrderHiveIsolate(__instance, victim);
                    }
                });

                result.Add(new Command_Target
                {
                    defaultLabel = "Hive: Assist ambush",
                    defaultDesc = "Order this hive agent to move in close and help restrain/distract an awake victim while another infected pawn performs the transfer.",
                    icon = TexCommand.Attack,
                    targetingParams = new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetSelf = false
                    },
                    action = delegate(LocalTargetInfo target)
                    {
                        Pawn victim = target.Pawn;
                        if (victim == null || victim.Dead || !victim.RaceProps.Humanlike || GameComponent_QuietHive.IsInfected(victim))
                        {
                            Find.Message("Choose an uninfected humanlike pawn.", MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        GameComponent_QuietHive.OrderHiveAmbushAssist(__instance, victim);
                    }
                });

                if (GameComponent_QuietHive.GetMind(__instance)?.broodCount > 0)
                {
                    result.Add(new Command_Action
                    {
                        defaultLabel = "Hive: Release juvenile",
                        defaultDesc = "Order this agent to release one mature juvenile at its current location.",
                        icon = TexCommand.Attack,
                        action = () => QuietHiveBroodUtility.SeedLooseParasite(__instance)
                    });
                }


                result.Add(new Command_Target
                {
                    defaultLabel = "Hive: Plant hidden parasite",
                    defaultDesc = "Choose a reachable floor or bed cell. This agent will secretly place one mature juvenile there. It stays hidden until it emerges, is discovered, or the hiding spot is disturbed.",
                    icon = TexCommand.Install,
                    targetingParams = new TargetingParameters
                    {
                        canTargetLocations = true
                    },
                    action = delegate(LocalTargetInfo target)
                    {
                        GameComponent_QuietHive.OrderPlantHiddenParasite(__instance, target.Cell);
                    }
                });

                if (__instance.drafter != null)
                {
                    result.Add(new Command_Toggle
                    {
                        defaultLabel = "Hive direct control",
                        defaultDesc = "Draft or release this infected pawn through the hive link. Their outward faction identity is not changed.",
                        icon = TexCommand.Draft,
                        isActive = () => __instance.Drafted,
                        toggleAction = () => __instance.drafter.Drafted = !__instance.drafter.Drafted
                    });
                }
            }

            result.Add(new Command_Action
            {
                defaultLabel = "Quiet Hive test",
                defaultDesc = "Run a medical screening for the hidden parasite. Early infections can produce false negatives; confirmed cases improve colony-wide investigation accuracy.",
                icon = TexCommand.MedicalRest,
                action = () => GameComponent_QuietHive.RunTest(__instance)
            });

            result.Add(new Command_Action
            {
                defaultLabel = "Quiet Hive investigation",
                defaultDesc = "View what the colony currently understands without revealing hidden infections.",
                icon = TexCommand.Inspect,
                action = () => Find.WindowStack.Add(new Dialog_Message(
                    "Investigation: " + Mathf.RoundToInt(gc.investigationProgress * 100f) + "%\n" +
                    "Parasite research: " + Mathf.RoundToInt(gc.parasiteResearch * 100f) + "%\n" +
                    "Confirmed cases: " + gc.confirmedCases + "\n" +
                    "Evidence examined: " + gc.evidenceFound + "\n" +
                    "Extracted adults: " + gc.adultParasitesExtracted + "\n" +
                    (gc.hiveEstablished ? "Colony status: Hive Established\n" : "") +
                    (gc.playerEmbracedHive ? "Path: Embrace the Hive" : "Path: Investigation/eradication")))
            });

            if (gc.hiveEstablished && !gc.playerEmbracedHive)
            {
                result.Add(new Command_Action
                {
                    defaultLabel = "Embrace the Hive",
                    defaultDesc = "Stop treating the established hive only as a hidden outbreak and deliberately cooperate with it.",
                    icon = TexCommand.ForbidOff,
                    action = () => GameComponent_QuietHive.EmbraceHive()
                });
            }

            if (GameComponent_QuietHive.IsInfected(__instance) &&
                !GameComponent_QuietHive.IsHiveControlled(__instance) &&
                GameComponent_QuietHive.GetMind(__instance)?.broodCount > 0)
            {
                result.Add(new Command_Action
                {
                    defaultLabel = "Release juvenile parasite",
                    defaultDesc = "Release one mature juvenile onto the map. It will seek a nearby sleeping or downed humanlike host, but can be spotted and destroyed while loose.",
                    icon = TexCommand.Attack,
                    action = () => QuietHiveBroodUtility.SeedLooseParasite(__instance)
                });
            }

            if (__instance.health?.hediffSet?.HasHediff(QuietHiveDefOf.QuietHive_Exposed) == true)
            {
                result.Add(new Command_Action
                {
                    defaultLabel = "Attempt parasite removal",
                    defaultDesc = "Attempt surgical removal. Better doctors improve success; long-established infections are harder to remove. Failure can injure the patient.",
                    icon = TexCommand.MedicalRest,
                    action = () => GameComponent_QuietHive.RunTreatment(__instance)
                });
            }

            __result = result;
        }
    }

    public class JobDriver_CovertInfect : JobDriver
    {
        private Pawn Victim => job.GetTarget(TargetIndex.A).Pawn;
        private bool victimWasAwake;
        private bool victimDetectedAttack;
        private bool grappleWon;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Victim, job, 1, -1, null, errorOnFailed);

        private enum InfectionRoute
        {
            Ear,
            Nose
        }

        private Vector3 FaceEntryPoint(Pawn victim, InfectionRoute route)
        {
            Vector3 p = victim.DrawPos;
            Vector3 forward;
            switch (victim.Rotation.AsInt)
            {
                case 0: forward = new Vector3(0f, 0f, 0.24f); break;   // north
                case 1: forward = new Vector3(0.24f, 0f, 0f); break;   // east
                case 2: forward = new Vector3(0f, 0f, -0.24f); break;  // south
                default: forward = new Vector3(-0.24f, 0f, 0f); break; // west
            }

            if (route == InfectionRoute.Nose)
                return p + forward * 0.72f;

            // Ear route ends slightly to one side of the head/pillow instead of at the centre.
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            return p + forward * 0.25f + side * 0.36f;
        }

        private void ShowParasiteCrawl(Pawn host, Pawn victim, InfectionRoute route)
        {
            if (host?.Map == null || victim?.Map != host.Map || QuietHiveDefOf.QuietHive_ParasiteMote == null) return;

            Vector3 from = host.DrawPos;
            Vector3 to = FaceEntryPoint(victim, route);

            // The first part hugs the floor/bed. The final sprites climb onto the pawn and end
            // beside the ear for sleepers or in front of the nose for restrained awake victims.
            for (int i = 1; i <= 8; i++)
            {
                float t = i / 9f;
                Vector3 pos = Vector3.Lerp(from, to, t);
                pos.y = (t < 0.70f ? AltitudeLayer.MoteLow : AltitudeLayer.MoteOverhead).AltitudeFor();
                float scale = t < 0.75f ? 0.70f : 0.58f;
                MoteMaker.MakeStaticMote(pos, host.Map, QuietHiveDefOf.QuietHive_ParasiteMote, scale);
            }
        }

        private float AwakeResistanceChance(Pawn host, Pawn victim)
        {
            float victimMelee = victim.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float hostMelee = host.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0f;
            float chance = 0.42f + (victimMelee - hostMelee) * 0.018f;

            // A weapon makes an alert victim substantially more dangerous to approach.
            if (victim.equipment?.Primary != null) chance += 0.18f;
            if (victim.IsPrisoner) chance -= 0.16f;

            int helpers = GameComponent_QuietHive.AmbushAssistantsNear(victim);
            chance -= helpers * 0.24f;

            if (victim.Downed) chance = 0.03f;
            return Mathf.Clamp(chance, 0.04f, 0.88f);
        }

        private void VictimDefends(Pawn victim, Pawn host)
        {
            if (victim == null || host == null || victim.Downed || victim.jobs == null) return;

            GameComponent_QuietHive.ExposeHost(host, victim, "attempted to force a parasite onto");
            GameComponent_QuietHive.SpawnEvidence(victim.Position, victim.Map, "failed infection");
            MoteMaker.ThrowText(victim.DrawPos, victim.Map, "PARASITE!", Color.red, 1.9f);

            // Use RimWorld's own combat jobs. Armed pawns can use AttackStatic (including ranged
            // weapons); unarmed pawns fall back to melee. This makes a failed awake infection
            // genuinely dangerous to the host rather than a cosmetic failure.
            JobDef retaliation = victim.equipment?.Primary != null ? JobDefOf.AttackStatic : JobDefOf.AttackMelee;
            Job fight = JobMaker.MakeJob(retaliation, host);
            victim.jobs.StartJob(fight, JobCondition.InterruptForced);

            HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(host);
            if (mind != null)
            {
                mind.suspicion = 1f;
                mind.lastWitnessedTick = Find.TickManager.TicksGame;
                mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(9000, 15000);
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Victim == null || Victim.Dead || GameComponent_QuietHive.IsInfected(Victim));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil assess = ToilMaker.MakeToil("QuietHive_AssessVictim");
            assess.initAction = delegate
            {
                victimWasAwake = Victim != null && Victim.Awake() && !Victim.Downed;
                victimDetectedAttack = false;
                grappleWon = false;
            };
            assess.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return assess;

            // Sleeping/downed targets get a slow, quiet transfer. Awake targets get only a short
            // surprise window before the victim can understand what is happening.
            Toil approach = Toils_General.Wait(120, TargetIndex.A);
            approach.WithProgressBarToilDelay(TargetIndex.A);
            approach.AddPreTickAction(() =>
            {
                if (Victim == null) { ReadyForNextToil(); return; }

                int witnesses = GameComponent_QuietHive.CountWitnesses(pawn, Victim);
                if (witnesses > 0)
                {
                    victimDetectedAttack = true;
                    GameComponent_QuietHive.Witnessed(pawn, Victim, witnesses);
                    ReadyForNextToil();
                    return;
                }

                // A sleeping victim who wakes during the setup immediately gets a chance to react.
                if (!victimWasAwake && Victim.Awake() && !Victim.Downed)
                {
                    victimWasAwake = true;
                    victimDetectedAttack = Rand.Chance(0.72f);
                    if (victimDetectedAttack) ReadyForNextToil();
                }
            });
            yield return approach;

            Toil struggle = ToilMaker.MakeToil("QuietHive_AwakeStruggle");
            struggle.initAction = delegate
            {
                Pawn victim = Victim;
                if (victim == null || victim.Dead) return;

                if (victimDetectedAttack)
                {
                    VictimDefends(victim, pawn);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (!victimWasAwake || victim.Downed)
                {
                    grappleWon = true;
                    return;
                }

                // Surprise can work, but an awake target has a real resistance roll. If they win,
                // they expose the host and immediately use their equipped weapon (or melee).
                if (Rand.Chance(AwakeResistanceChance(pawn, victim)))
                {
                    VictimDefends(victim, pawn);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                grappleWon = true;
                MoteMaker.ThrowText(victim.DrawPos, victim.Map, "struggling...", Color.white, 0.8f);
            };
            struggle.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return struggle;

            Toil transfer = Toils_General.Wait(90, TargetIndex.A);
            transfer.WithProgressBarToilDelay(TargetIndex.A);
            transfer.AddPreTickAction(() =>
            {
                if (Victim == null || !grappleWon) { ReadyForNextToil(); return; }

                // A sleeper who wakes during the final ear-entry phase can knock the juvenile loose.
                if (!victimWasAwake && Victim.Awake() && !Victim.Downed)
                {
                    if (Rand.Chance(0.68f))
                    {
                        Thing loose = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_JuvenileParasite);
                        GenSpawn.Spawn(loose, Victim.Position, Victim.Map);
                        GameComponent_QuietHive.SpawnEvidence(Victim.Position, Victim.Map, "interrupted ear entry");
                        MoteMaker.ThrowText(Victim.DrawPos, Victim.Map, "something crawled on me!", Color.yellow, 1.2f);
                        grappleWon = false;
                        ReadyForNextToil();
                        return;
                    }
                    victimWasAwake = true;
                }

                // An awake victim can still break free while the parasite is crawling up them.
                if (victimWasAwake && Find.TickManager.TicksGame % 30 == 0 &&
                    Rand.Chance(AwakeResistanceChance(pawn, Victim) * 0.12f))
                {
                    VictimDefends(Victim, pawn);
                    grappleWon = false;
                    ReadyForNextToil();
                    return;
                }

                int witnesses = GameComponent_QuietHive.CountWitnesses(pawn, Victim);
                if (witnesses > 0)
                {
                    GameComponent_QuietHive.Witnessed(pawn, Victim, witnesses);
                    grappleWon = false;
                    ReadyForNextToil();
                }
            });
            yield return transfer;

            Toil infect = ToilMaker.MakeToil("QuietHive_Infect");
            infect.initAction = delegate
            {
                Pawn victim = Victim;
                if (victim == null || victim.Dead || !grappleWon || GameComponent_QuietHive.IsInfected(victim)) return;

                int witnesses = GameComponent_QuietHive.CountWitnesses(pawn, victim);
                if (witnesses > 0)
                {
                    GameComponent_QuietHive.Witnessed(pawn, victim, witnesses);
                    ShowParasiteCrawl(pawn, victim, victimWasAwake ? InfectionRoute.Nose : InfectionRoute.Ear);
                    return;
                }

                // Physical sequence: host places/releases the juvenile at touching distance.
                // Sleepers use the quiet ear route; restrained awake victims use the nose route.
                // The sprite disappears at the selected face entry point.
                ShowParasiteCrawl(pawn, victim, victimWasAwake ? InfectionRoute.Nose : InfectionRoute.Ear);
                victim.health.AddHediff(QuietHiveDefOf.QuietHive_Infection);
                GameComponent_QuietHive.OnPawnInfected(victim, pawn);

                HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(pawn);
                if (mind != null)
                {
                    mind.infectionsCaused++;
                    mind.broodCount = Math.Max(0, mind.broodCount - 1);
                    mind.suspicion = Math.Max(0f, mind.suspicion - 0.08f);
                    GameComponent_QuietHive hiveGame = Current.Game.GetComponent<GameComponent_QuietHive>();
                    hiveGame.hiveIntelligence = Mathf.Clamp01(hiveGame.hiveIntelligence + 0.025f);
                    if (victim.Faction != Faction.OfPlayer)
                    {
                        hiveGame.outsideInfections++;
                        if (victim.Faction != null && hiveGame.outsideInfections % 3 == 0)
                            hiveGame.factionsSeeded = Math.Max(hiveGame.factionsSeeded, hiveGame.outsideInfections / 3);
                    }
                }

                // A successfully controlled awake victim stops resisting and resumes normal AI.
                // Sleeping victims are not deliberately woken by this job.
                if (victimWasAwake && victim.jobs != null && !victim.Downed)
                    victim.jobs.EndCurrentJob(JobCondition.InterruptOptional);

                MoteMaker.ThrowText(victim.DrawPos, victim.Map, "...", 0.55f);
            };
            infect.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return infect;
        }
    }




    public class Thing_AdultParasite : ThingWithComps
    {
        public override string GetInspectString()
        {
            GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
            return base.GetInspectString() + "\nAn extracted adult Quiet Hive parasite. Valuable for study, but dangerous if containment is poor." +
                "\nResearch understanding: " + Mathf.RoundToInt(gc.parasiteResearch * 100f) + "%";
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "Study parasite",
                defaultDesc = "Study this extracted adult specimen. Research improves diagnostic sensitivity and removal success.",
                icon = TexCommand.Inspect,
                action = () => GameComponent_QuietHive.StudySpecimen(this)
            };
        }
    }

    public class Building_QuietHiveContainment : Building
    {
        private int storedSpecimens;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref storedSpecimens, "quietHiveStoredSpecimens", 0);
        }

        public override string GetInspectString()
        {
            string power = this.TryGetComp<CompPowerTrader>()?.PowerOn == true ? "powered" : "UNPOWERED";
            return base.GetInspectString() + "\nParasite specimens: " + storedSpecimens + "\nContainment: " + power;
        }

        public override void TickRare()
        {
            base.TickRare();
            CompPowerTrader power = this.TryGetComp<CompPowerTrader>();
            if (storedSpecimens > 0 && power != null && !power.PowerOn && Rand.Chance(0.035f))
            {
                Thing adult = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_AdultParasite);
                GenSpawn.Spawn(adult, Position, Map);
                storedSpecimens = Math.Max(0, storedSpecimens - 1);
                Find.LetterStack.ReceiveLetter(
                    "Containment failure",
                    "A Quiet Hive specimen escaped from an unpowered containment pod.",
                    LetterDefOf.ThreatSmall,
                    this);
            }
        }
    }

    public class Thing_QuietHiveEvidence : ThingWithComps
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Examine parasite evidence",
                defaultDesc = "Examine this strange shed skin/slime residue. Doing so advances the colony's investigation.",
                icon = TexCommand.Inspect,
                action = delegate
                {
                    GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
                    gc.evidenceFound++;
                    float gain = def == QuietHiveDefOf.QuietHive_DeadJuvenile ? 0.18f :
                                 def == QuietHiveDefOf.QuietHive_ShedSkin ? 0.10f :
                                 def == QuietHiveDefOf.QuietHive_SlimeEvidence ? 0.08f : 0.12f;
                    gc.investigationProgress = Mathf.Clamp01(gc.investigationProgress + gain);
                    gc.parasiteResearch = Mathf.Clamp01(gc.parasiteResearch + gain * 0.30f);
                    Find.Message("The evidence adds to the colony's understanding of the physical parasite.", MessageTypeDefOf.PositiveEvent, false);
                    Destroy(DestroyMode.Vanish);
                }
            };
        }
    }

    public class Thing_JuvenileParasite : ThingWithComps
    {
        private int nextThinkTick;

        public override void Tick()
        {
            base.Tick();
            if (!Spawned || Map == null || Find.TickManager.TicksGame < nextThinkTick) return;
            nextThinkTick = Find.TickManager.TicksGame + 90;

            // If an awake uninfected pawn is very close, the juvenile freezes and plays dead.
            bool observedClose = Map.mapPawns.AllPawnsSpawned.Any(p =>
                p.RaceProps.Humanlike && !p.Dead && p.Awake() && !GameComponent_QuietHive.IsInfected(p) &&
                Position.DistanceTo(p.Position) <= 5f && GenSight.LineOfSight(Position, p.Position, Map));
            if (observedClose) return;

            Pawn target = Map.mapPawns.AllPawnsSpawned
                .Where(p => p.RaceProps.Humanlike && !p.Dead && !GameComponent_QuietHive.IsInfected(p))
                .Where(p => Position.DistanceTo(p.Position) <= 12f)
                .OrderByDescending(p => (!p.Awake() ? 30f : 0f) + (p.Downed ? 30f : 0f) - Position.DistanceTo(p.Position))
                .FirstOrDefault();

            if (target == null) return;

            // Loose juveniles are deliberately vulnerable and visible. They move only a little at
            // a time, giving colonists a chance to spot/kill them before they reach a sleeper.
            IntVec3 step = GenAdj.CellsAdjacent8Way(target).Where(c => c.InBounds(Map) && c.Standable(Map))
                .OrderBy(c => c.DistanceTo(Position)).FirstOrDefault();
            if (step.IsValid && Position.DistanceTo(target.Position) > 1.5f)
            {
                IntVec3 toward = Position + (target.Position - Position).ClampInsideMap(Map).Sign();
                if (toward.InBounds(Map) && toward.Standable(Map))
                    Position = toward;
                return;
            }

            if (!target.Awake() || target.Downed)
            {
                target.health.AddHediff(QuietHiveDefOf.QuietHive_Infection);
                GameComponent_QuietHive.OnPawnInfected(target, null);
                GameComponent_QuietHive.SpawnEvidence(Position, Map, "loose parasite transfer");
                MoteMaker.ThrowText(target.DrawPos, Map, "...", 0.55f);
                Destroy(DestroyMode.Vanish);
            }
        }
    }

    public static class QuietHiveBroodUtility
    {
        public static void SeedLooseParasite(Pawn host)
        {
            HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(host);
            if (mind == null || mind.broodCount <= 0 || host?.Map == null) return;
            Thing parasite = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_JuvenileParasite);
            GenSpawn.Spawn(parasite, host.Position, host.Map);
            mind.broodCount--;
            mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(4000, 7000);
        }
    }


    public class JobDriver_DoctorDeception : JobDriver
    {
        private Pawn Patient => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => Patient != null && pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Patient == null || Patient.Dead || GameComponent_QuietHive.IsInfected(Patient));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil examine = Toils_General.Wait(240, TargetIndex.A);
            examine.socialMode = RandomSocialMode.SuperActive;
            examine.WithProgressBarToilDelay(TargetIndex.A);
            yield return examine;

            Toil decide = ToilMaker.MakeToil("QuietHive_DoctorDecision");
            decide.initAction = delegate
            {
                if (Patient == null || Patient.Dead || GameComponent_QuietHive.IsInfected(Patient)) return;

                // The doctor's legitimate presence explains the approach, but the physical worm
                // still cannot be released while an alert third party has line of sight.
                int witnesses = GameComponent_QuietHive.CountWitnesses(pawn, Patient);
                if (witnesses == 0 || !Patient.Awake())
                {
                    Job infect = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_CovertInfect, Patient);
                    pawn.jobs.StartJob(infect, JobCondition.Succeeded);
                }
                else
                {
                    HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(pawn);
                    if (mind != null)
                        mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(2400, 4800);
                }
            };
            decide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return decide;
        }
    }

    public class JobDriver_AmbushAssist : JobDriver
    {
        private Pawn Victim => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Dead || GameComponent_QuietHive.IsInfected(Victim));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil assist = Toils_General.Wait(360, TargetIndex.A);
            assist.socialMode = RandomSocialMode.SuperActive;
            assist.AddPreTickAction(() =>
            {
                if (Victim == null || Victim.Dead || GameComponent_QuietHive.IsInfected(Victim))
                    ReadyForNextToil();
            });
            yield return assist;
        }
    }


    public class JobDriver_HideInBed : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            IntVec3 cell = job.GetTarget(TargetIndex.A).Cell;
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil hide = ToilMaker.MakeToil("QuietHive_HideParasite");
            hide.initAction = delegate
            {
                HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(pawn);
                if (mind == null || mind.broodCount <= 0 || pawn.Map == null) return;
                Thing parasite = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_JuvenileParasite);
                GenSpawn.Spawn(parasite, cell, pawn.Map);
                mind.broodCount--;
                mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(5000, 8000);
            };
            hide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return hide;
        }
    }

    public class JobDriver_RetrieveParasite : JobDriver
    {
        private Thing Parasite => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Parasite == null || Parasite.Destroyed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil retrieve = ToilMaker.MakeToil("QuietHive_RetrieveParasite");
            retrieve.initAction = delegate
            {
                if (Parasite == null || Parasite.Destroyed) return;
                HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(pawn);
                if (mind == null) return;

                int cap = mind.InfectionDays >= 8f ? 4 : 2;
                if (mind.broodCount < cap)
                {
                    mind.broodCount++;
                    Parasite.Destroy(DestroyMode.Vanish);
                    mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(1800, 3200);
                }
            };
            retrieve.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return retrieve;
        }
    }


    public class JobDriver_DestroyEvidence : JobDriver
    {
        private Thing Evidence => job.GetTarget(TargetIndex.A).Thing;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Evidence == null || Evidence.Destroyed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil t = ToilMaker.MakeToil("QuietHive_DestroyEvidence");
            t.initAction = delegate
            {
                if (Evidence != null && !Evidence.Destroyed) Evidence.Destroy(DestroyMode.Vanish);
            };
            t.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return t;
        }
    }

    public class JobDriver_RescueExposed : JobDriver
    {
        private Pawn Ally => job.GetTarget(TargetIndex.A).Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Ally == null || Ally.Dead || !GameComponent_QuietHive.IsInfected(Ally));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil t = Toils_General.Wait(180, TargetIndex.A);
            t.socialMode = RandomSocialMode.SuperActive;
            t.AddFinishAction(() =>
            {
                Hediff exposed = Ally.health?.hediffSet?.GetFirstHediffOfDef(QuietHiveDefOf.QuietHive_Exposed);
                if (exposed != null && Rand.Chance(0.35f))
                {
                    // The hive cannot erase medical truth, but it can reduce immediate suspicion by
                    // moving/covering for an exposed host. Here that is represented by lowering
                    // investigation momentum rather than deleting the exposed hediff.
                    GameComponent_QuietHive gc = Current.Game.GetComponent<GameComponent_QuietHive>();
                    gc.investigationProgress = Math.Max(0f, gc.investigationProgress - 0.05f);
                }
            });
            yield return t;
        }
    }


    public class Thing_HiddenParasite : ThingWithComps
    {
        private int nextCheckTick;
        private int plantedTick;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextCheckTick, "quietHiveHiddenNextCheckTick", 0);
            Scribe_Values.Look(ref plantedTick, "quietHiveHiddenPlantedTick", 0);
        }

        public override void PostMake()
        {
            base.PostMake();
            plantedTick = Find.TickManager?.TicksGame ?? 0;
        }

        public override bool HiddenFromPlayer => true;

        public override void Tick()
        {
            base.Tick();
            if (!Spawned || Map == null || Find.TickManager.TicksGame < nextCheckTick) return;
            nextCheckTick = Find.TickManager.TicksGame + 120;

            // Wait for a sleeping/downed uninfected humanlike pawn very close to the hiding place.
            Pawn target = Map.mapPawns.AllPawnsSpawned
                .Where(p => p.RaceProps.Humanlike && !p.Dead && !GameComponent_QuietHive.IsInfected(p))
                .Where(p => Position.DistanceTo(p.Position) <= 2.2f && (!p.Awake() || p.Downed))
                .OrderBy(p => Position.DistanceTo(p.Position))
                .FirstOrDefault();

            if (target == null) return;

            Thing juvenile = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_JuvenileParasite);
            GenSpawn.Spawn(juvenile, Position, Map);
            Destroy(DestroyMode.Vanish);
        }

        public override string GetInspectString()
        {
            return "A concealed Quiet Hive juvenile is hidden here.";
        }
    }

    public class JobDriver_PlantHiddenParasite : JobDriver
    {
        private IntVec3 Cell => job.GetTarget(TargetIndex.A).Cell;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            Toil plant = Toils_General.Wait(150, TargetIndex.A);
            plant.WithProgressBarToilDelay(TargetIndex.A);
            yield return plant;

            Toil finish = ToilMaker.MakeToil("QuietHive_PlantHiddenParasite");
            finish.initAction = delegate
            {
                HediffComp_QuietHiveMind mind = GameComponent_QuietHive.GetMind(pawn);
                if (mind == null || mind.broodCount <= 0 || pawn.Map == null) return;

                Thing hidden = ThingMaker.MakeThing(QuietHiveDefOf.QuietHive_HiddenParasite);
                GenSpawn.Spawn(hidden, Cell, pawn.Map);

                mind.broodCount--;
                mind.nextAttemptTick = Find.TickManager.TicksGame + Rand.RangeInclusive(3500, 6000);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }

    public class JobDriver_Lure : JobDriver
    {
        private Pawn TargetPawn => job.GetTarget(TargetIndex.A).Pawn;
        private IntVec3 Destination => job.GetTarget(TargetIndex.B).Cell;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => TargetPawn == null || TargetPawn.Dead || GameComponent_QuietHive.IsInfected(TargetPawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil invite = Toils_General.Wait(90, TargetIndex.A);
            invite.socialMode = RandomSocialMode.SuperActive;
            invite.initAction = delegate
            {
                if (TargetPawn?.jobs != null && !TargetPawn.Drafted && !TargetPawn.Downed && !TargetPawn.InMentalState)
                {
                    Job follow = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_LuredFollow, pawn);
                    TargetPawn.jobs.StartJob(follow, JobCondition.InterruptOptional);
                }
            };
            yield return invite;
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil pause = Toils_General.Wait(150);
            pause.socialMode = RandomSocialMode.SuperActive;
            pause.AddFinishAction(() =>
            {
                if (TargetPawn != null && TargetPawn.Spawned && TargetPawn.Position.DistanceTo(pawn.Position) <= 3f &&
                    GameComponent_QuietHive.CountWitnesses(pawn, TargetPawn) == 0 && !GameComponent_QuietHive.IsInfected(TargetPawn))
                {
                    Job infect = JobMaker.MakeJob(QuietHiveDefOf.QuietHive_CovertInfect, TargetPawn);
                    pawn.jobs.StartJob(infect, JobCondition.Succeeded);
                }
            });
            yield return pause;
        }
    }

    public class JobDriver_LuredFollow : JobDriver
    {
        private Pawn Host => job.GetTarget(TargetIndex.A).Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Host == null || Host.Dead || !Host.Spawned || !GameComponent_QuietHive.IsInfected(Host));
            Toil follow = ToilMaker.MakeToil("QuietHive_FollowHost");
            follow.defaultCompleteMode = ToilCompleteMode.Delay;
            follow.defaultDuration = 900;
            follow.tickAction = delegate
            {
                if (Host == null || !Host.Spawned) { ReadyForNextToil(); return; }
                if (pawn.Position.DistanceTo(Host.Position) > 3f && pawn.pather != null && !pawn.pather.Moving)
                    pawn.pather.StartPath(Host, PathEndMode.Touch);
                if (Host.CurJobDef != QuietHiveDefOf.QuietHive_Lure) ReadyForNextToil();
            };
            yield return follow;
        }
    }

    public class JobDriver_Cover : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil cover = Toils_General.Wait(300, TargetIndex.A);
            cover.socialMode = RandomSocialMode.SuperActive;
            yield return cover;
        }
    }
}