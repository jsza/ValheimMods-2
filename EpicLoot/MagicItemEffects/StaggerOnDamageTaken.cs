﻿using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace EpicLoot.MagicItemEffects
{
	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	public class RPC_StaggerOnDamageTaken_Character_RPC_Damage_Patch
	{
		[UsedImplicitly]
		private static void Postfix(Character __instance, HitData hit)
		{
			Character attacker = hit.GetAttacker();
			if (__instance is Player player && player.HasMagicEquipmentWithEffect(MagicEffectType.StaggerOnDamageTaken, out var items) && attacker != null && attacker != __instance && !attacker.IsStaggering())
			{
				var staggerChance = 0f;
				foreach (var item in items)
				{
					staggerChance += item.GetMagicItem().GetTotalEffectValue(MagicEffectType.StaggerOnDamageTaken, 0.01f);
				}

				if (Random.Range(0f, 1f) < staggerChance)
				{
					attacker.Stagger(-attacker.transform.forward);
				}
			}
		}
	}
}