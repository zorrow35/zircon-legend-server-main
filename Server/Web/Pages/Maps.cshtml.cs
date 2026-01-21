using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Server.Envir;
using Library.SystemModels;
using Library;
using System.Collections.Generic;
using System.Linq;

namespace Server.Web.Pages
{
    [Authorize]
    public class MapsModel : PageModel
    {
        public List<MapViewModel> Maps { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Keyword { get; set; }

        public string? Message { get; set; }

        public void OnGet()
        {
            LoadMaps();
        }

        private void LoadMaps()
        {
            try
            {
                foreach (var kvp in SEnvir.Maps.ToList())
                {
                    var mapInfo = kvp.Key;
                    var map = kvp.Value;

                    if (!string.IsNullOrWhiteSpace(Keyword))
                    {
                        if (!(mapInfo.Description?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) &&
                            !(mapInfo.FileName?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false))
                        {
                            continue;
                        }
                    }

                    Maps.Add(new MapViewModel
                    {
                        Index = mapInfo.Index,
                        FileName = mapInfo.FileName ?? "",
                        Description = mapInfo.Description ?? "",
                        MinimumLevel = mapInfo.MinimumLevel,
                        MaximumLevel = mapInfo.MaximumLevel,
                        PlayerCount = map.Players?.Count ?? 0,
                        MonsterCount = map.Objects?.Count(o => o is Zircon.Server.Models.MonsterObject) ?? 0,
                        Width = map.Width,
                        Height = map.Height,
                        AllowRT = !mapInfo.AllowRT ? "禁止" : "允许"
                    });
                }

                Maps = Maps.OrderByDescending(m => m.PlayerCount).ThenBy(m => m.Description).ToList();
            }
            catch
            {
                // Prevent enumeration errors
            }
        }

        public IActionResult OnPostTeleport(string playerName, int mapIndex, int x, int y)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                Message = "权限不足，需要 Admin 权限";
                LoadMaps();
                return Page();
            }

            try
            {
                var player = SEnvir.Players.FirstOrDefault(p =>
                    p?.Character?.CharacterName?.Equals(playerName, System.StringComparison.OrdinalIgnoreCase) == true);

                if (player == null)
                {
                    Message = $"玩家 {playerName} 不在线";
                    LoadMaps();
                    return Page();
                }

                var mapEntry = SEnvir.Maps.FirstOrDefault(m => m.Key.Index == mapIndex);
                if (mapEntry.Value == null)
                {
                    Message = $"地图 {mapIndex} 不存在";
                    LoadMaps();
                    return Page();
                }

                var targetMap = mapEntry.Value;
                var location = new System.Drawing.Point(x, y);

                // If x,y is 0,0, find a valid spawn point
                if (x <= 0 || y <= 0)
                {
                    location = targetMap.GetRandomLocation();
                }

                player.Teleport(targetMap, location);
                Message = $"已将 {playerName} 传送到 {mapEntry.Key.Description} ({location.X}, {location.Y})";
            }
            catch (System.Exception ex)
            {
                Message = $"传送失败: {ex.Message}";
            }

            LoadMaps();
            return Page();
        }

        public IActionResult OnPostBroadcast(string message)
        {
            if (!HasPermission(AccountIdentity.Operator))
            {
                Message = "权限不足，需要 Operator 权限";
                LoadMaps();
                return Page();
            }

            try
            {
                foreach (var player in SEnvir.Players.ToList())
                {
                    player?.Connection?.ReceiveChat($"[系统公告] {message}", Library.MessageType.Announcement);
                }
                Message = $"已发送全服公告: {message}";
            }
            catch (System.Exception ex)
            {
                Message = $"发送失败: {ex.Message}";
            }

            LoadMaps();
            return Page();
        }

        // 获取地图详情 (AJAX)
        public IActionResult OnGetMapDetail(int mapIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var mapInfo = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                if (mapInfo == null)
                {
                    return new JsonResult(new { success = false, message = "地图不存在" });
                }

                var detail = new MapDetailViewModel
                {
                    Index = mapInfo.Index,
                    FileName = mapInfo.FileName ?? "",
                    Description = mapInfo.Description ?? "",
                    MiniMap = mapInfo.MiniMap,
                    Light = (int)mapInfo.Light,
                    Fight = (int)mapInfo.Fight,
                    AllowRT = mapInfo.AllowRT,
                    AllowTT = mapInfo.AllowTT,
                    CanHorse = mapInfo.CanHorse,
                    CanMine = mapInfo.CanMine,
                    CanMarriageRecall = mapInfo.CanMarriageRecall,
                    AllowRecall = mapInfo.AllowRecall,
                    MinimumLevel = mapInfo.MinimumLevel,
                    MaximumLevel = mapInfo.MaximumLevel,
                    MonsterHealth = mapInfo.MonsterHealth,
                    MaxMonsterHealth = mapInfo.MaxMonsterHealth,
                    MonsterDamage = mapInfo.MonsterDamage,
                    MaxMonsterDamage = mapInfo.MaxMonsterDamage,
                    DropRate = mapInfo.DropRate,
                    MaxDropRate = mapInfo.MaxDropRate,
                    ExperienceRate = mapInfo.ExperienceRate,
                    MaxExperienceRate = mapInfo.MaxExperienceRate,
                    GoldRate = mapInfo.GoldRate,
                    MaxGoldRate = mapInfo.MaxGoldRate,
                    SkillDelay = mapInfo.SkillDelay
                };

                return new JsonResult(new { success = true, data = detail });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 新建地图
        public IActionResult OnPostCreateMap(
            string fileName,
            string description,
            int miniMap,
            int light,
            int fight,
            bool allowRT,
            bool allowTT,
            bool canHorse,
            bool canMine,
            bool canMarriageRecall,
            bool allowRecall,
            int minimumLevel,
            int maximumLevel,
            int monsterHealth,
            int maxMonsterHealth,
            int monsterDamage,
            int maxMonsterDamage,
            int dropRate,
            int maxDropRate,
            int experienceRate,
            int maxExperienceRate,
            int goldRate,
            int maxGoldRate,
            int skillDelay)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                Message = "权限不足，需要 SuperAdmin 权限";
                LoadMaps();
                return Page();
            }

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Message = "地图文件名不能为空";
                    LoadMaps();
                    return Page();
                }

                var newMap = SEnvir.MapInfoList?.CreateNewObject();
                if (newMap == null)
                {
                    Message = "创建地图失败";
                    LoadMaps();
                    return Page();
                }

                newMap.FileName = fileName;
                newMap.Description = string.IsNullOrWhiteSpace(description) ? fileName : description;
                newMap.MiniMap = miniMap;
                newMap.Light = (LightSetting)light;
                newMap.Fight = (FightSetting)fight;
                newMap.AllowRT = allowRT;
                newMap.AllowTT = allowTT;
                newMap.CanHorse = canHorse;
                newMap.CanMine = canMine;
                newMap.CanMarriageRecall = canMarriageRecall;
                newMap.AllowRecall = allowRecall;
                newMap.MinimumLevel = minimumLevel;
                newMap.MaximumLevel = maximumLevel;
                newMap.MonsterHealth = monsterHealth;
                newMap.MaxMonsterHealth = maxMonsterHealth;
                newMap.MonsterDamage = monsterDamage;
                newMap.MaxMonsterDamage = maxMonsterDamage;
                newMap.DropRate = dropRate;
                newMap.MaxDropRate = maxDropRate;
                newMap.ExperienceRate = experienceRate;
                newMap.MaxExperienceRate = maxExperienceRate;
                newMap.GoldRate = goldRate;
                newMap.MaxGoldRate = maxGoldRate;
                newMap.SkillDelay = skillDelay;

                Message = $"地图 [{newMap.Index}] {description} 创建成功（需重启服务器加载地图文件）";
                SEnvir.Log($"[Admin] 新建地图: [{newMap.Index}] {description} ({fileName})");
            }
            catch (System.Exception ex)
            {
                Message = $"创建失败: {ex.Message}";
            }

            LoadMaps();
            return Page();
        }

        // 编辑地图
        public IActionResult OnPostUpdateMap(
            int mapIndex,
            string fileName,
            string description,
            int miniMap,
            int light,
            int fight,
            bool allowRT,
            bool allowTT,
            bool canHorse,
            bool canMine,
            bool canMarriageRecall,
            bool allowRecall,
            int minimumLevel,
            int maximumLevel,
            int monsterHealth,
            int maxMonsterHealth,
            int monsterDamage,
            int maxMonsterDamage,
            int dropRate,
            int maxDropRate,
            int experienceRate,
            int maxExperienceRate,
            int goldRate,
            int maxGoldRate,
            int skillDelay)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                Message = "权限不足，需要 SuperAdmin 权限";
                LoadMaps();
                return Page();
            }

            try
            {
                var mapInfo = SEnvir.MapInfoList?.Binding?.FirstOrDefault(m => m.Index == mapIndex);
                if (mapInfo == null)
                {
                    Message = $"地图索引 {mapIndex} 不存在";
                    LoadMaps();
                    return Page();
                }

                var oldName = mapInfo.Description;

                mapInfo.FileName = fileName;
                mapInfo.Description = description;
                mapInfo.MiniMap = miniMap;
                mapInfo.Light = (LightSetting)light;
                mapInfo.Fight = (FightSetting)fight;
                mapInfo.AllowRT = allowRT;
                mapInfo.AllowTT = allowTT;
                mapInfo.CanHorse = canHorse;
                mapInfo.CanMine = canMine;
                mapInfo.CanMarriageRecall = canMarriageRecall;
                mapInfo.AllowRecall = allowRecall;
                mapInfo.MinimumLevel = minimumLevel;
                mapInfo.MaximumLevel = maximumLevel;
                mapInfo.MonsterHealth = monsterHealth;
                mapInfo.MaxMonsterHealth = maxMonsterHealth;
                mapInfo.MonsterDamage = monsterDamage;
                mapInfo.MaxMonsterDamage = maxMonsterDamage;
                mapInfo.DropRate = dropRate;
                mapInfo.MaxDropRate = maxDropRate;
                mapInfo.ExperienceRate = experienceRate;
                mapInfo.MaxExperienceRate = maxExperienceRate;
                mapInfo.GoldRate = goldRate;
                mapInfo.MaxGoldRate = maxGoldRate;
                mapInfo.SkillDelay = skillDelay;

                Message = $"地图 [{mapIndex}] {description} 已更新";
                SEnvir.Log($"[Admin] 修改地图: [{mapIndex}] {oldName} -> {description}");
            }
            catch (System.Exception ex)
            {
                Message = $"修改失败: {ex.Message}";
            }

            LoadMaps();
            return Page();
        }

        private bool HasPermission(AccountIdentity required)
        {
            var permissionClaim = User.FindFirst("Permission")?.Value;
            if (string.IsNullOrEmpty(permissionClaim)) return false;

            if (int.TryParse(permissionClaim, out int permValue))
            {
                return permValue >= (int)required;
            }
            return false;
        }

        // ==================== 地图怪物管理功能 ====================

        // 获取地图的区域列表
        public IActionResult OnGetMapRegions(int mapIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var regions = SEnvir.MapRegionList?.Binding
                    ?.Where(r => r.Map?.Index == mapIndex)
                    ?.Select(r => new MapRegionViewModel
                    {
                        RegionIndex = r.Index,
                        Description = r.Description ?? "",
                        Size = r.Size
                    })
                    ?.OrderBy(r => r.Description)
                    ?.ToList()
                    ?? new List<MapRegionViewModel>();

                return new JsonResult(new { success = true, data = regions });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取区域的怪物列表
        public IActionResult OnGetRegionMonsters(int regionIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Index == regionIndex);
                if (region == null)
                {
                    return new JsonResult(new { success = false, message = "区域不存在" });
                }

                var monsters = SEnvir.RespawnInfoList?.Binding
                    ?.Where(r => r.Region?.Index == regionIndex)
                    ?.Select(r => new RegionMonsterViewModel
                    {
                        RespawnIndex = r.Index,
                        MonsterIndex = r.Monster?.Index ?? 0,
                        MonsterName = r.Monster?.MonsterName ?? "Unknown",
                        MonsterLevel = r.Monster?.Level ?? 0,
                        Delay = r.Delay,
                        Count = r.Count,
                        DropSet = r.DropSet,
                        EventSpawn = r.EventSpawn,
                        Announce = r.Announce,
                        EasterEventChance = r.EasterEventChance
                    })
                    ?.OrderBy(r => r.MonsterName)
                    ?.ToList()
                    ?? new List<RegionMonsterViewModel>();

                return new JsonResult(new { success = true, data = monsters, regionName = region.Description });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取怪物列表（用于选择）
        public IActionResult OnGetMonstersList(string? keyword = "")
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var query = SEnvir.MonsterInfoList?.Binding?.AsEnumerable()
                    ?? Enumerable.Empty<MonsterInfo>();

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    query = query.Where(m =>
                        (m.MonsterName?.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        m.Index.ToString().Contains(keyword));
                }

                var monsters = query.Take(100)
                    .Select(m => new MonsterSelectOption
                    {
                        Index = m.Index,
                        Name = m.MonsterName ?? "Unknown",
                        Level = m.Level,
                        Image = (int)m.Image
                    })
                    .OrderBy(m => m.Name)
                    .ToList();

                return new JsonResult(new { success = true, data = monsters });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 添加怪物到区域
        public IActionResult OnPostAddMonster(
            int regionIndex,
            int monsterIndex,
            int delay,
            int count,
            int dropSet,
            bool eventSpawn,
            bool announce,
            int easterEventChance)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var region = SEnvir.MapRegionList?.Binding?.FirstOrDefault(r => r.Index == regionIndex);
                if (region == null)
                {
                    return new JsonResult(new { success = false, message = "区域不存在" });
                }

                var monster = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monster == null)
                {
                    return new JsonResult(new { success = false, message = $"怪物索引 {monsterIndex} 不存在" });
                }

                var respawn = SEnvir.RespawnInfoList?.CreateNewObject();
                if (respawn == null)
                {
                    return new JsonResult(new { success = false, message = "创建怪物配置失败" });
                }

                respawn.Region = region;
                respawn.Monster = monster;
                respawn.Delay = delay > 0 ? delay : 60;
                respawn.Count = count > 0 ? count : 1;
                respawn.DropSet = dropSet;
                respawn.EventSpawn = eventSpawn;
                respawn.Announce = announce;
                respawn.EasterEventChance = easterEventChance;

                SEnvir.Log($"[Admin] 添加怪物到区域 [{regionIndex}] {region.Description}: {count}x {monster.MonsterName} (延迟: {delay}s)");

                return new JsonResult(new { success = true, message = $"已添加 {monster.MonsterName} 到区域 {region.Description}" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"添加失败: {ex.Message}" });
            }
        }

        // 更新怪物配置
        public IActionResult OnPostUpdateMonster(
            int respawnIndex,
            int delay,
            int count,
            int dropSet,
            bool eventSpawn,
            bool announce,
            int easterEventChance)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var respawn = SEnvir.RespawnInfoList?.Binding?.FirstOrDefault(r => r.Index == respawnIndex);
                if (respawn == null)
                {
                    return new JsonResult(new { success = false, message = $"怪物配置 {respawnIndex} 不存在" });
                }

                respawn.Delay = delay > 0 ? delay : 60;
                respawn.Count = count > 0 ? count : 1;
                respawn.DropSet = dropSet;
                respawn.EventSpawn = eventSpawn;
                respawn.Announce = announce;
                respawn.EasterEventChance = easterEventChance;

                SEnvir.Log($"[Admin] 更新怪物配置 [{respawnIndex}] {respawn.Monster?.MonsterName}");

                return new JsonResult(new { success = true, message = "怪物配置已更新" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"更新失败: {ex.Message}" });
            }
        }

        // 删除怪物配置
        public IActionResult OnPostDeleteMonster(int respawnIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var respawn = SEnvir.RespawnInfoList?.Binding?.FirstOrDefault(r => r.Index == respawnIndex);
                if (respawn == null)
                {
                    return new JsonResult(new { success = false, message = $"怪物配置 {respawnIndex} 不存在" });
                }

                var monsterName = respawn.Monster?.MonsterName ?? "Unknown";
                var regionName = respawn.Region?.Description ?? "";

                respawn.Delete();

                SEnvir.Log($"[Admin] 删除怪物配置 [{respawnIndex}] {monsterName} from {regionName}");

                return new JsonResult(new { success = true, message = $"已删除怪物 {monsterName}" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"删除失败: {ex.Message}" });
            }
        }
    }

    public class MapViewModel
    {
        public int Index { get; set; }
        public string FileName { get; set; } = "";
        public string Description { get; set; } = "";
        public int MinimumLevel { get; set; }
        public int MaximumLevel { get; set; }
        public int PlayerCount { get; set; }
        public int MonsterCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string AllowRT { get; set; } = "";
    }

    public class MapDetailViewModel
    {
        public int Index { get; set; }
        public string FileName { get; set; } = "";
        public string Description { get; set; } = "";
        public int MiniMap { get; set; }
        public int Light { get; set; }
        public int Fight { get; set; }
        public bool AllowRT { get; set; }
        public bool AllowTT { get; set; }
        public bool CanHorse { get; set; }
        public bool CanMine { get; set; }
        public bool CanMarriageRecall { get; set; }
        public bool AllowRecall { get; set; }
        public int MinimumLevel { get; set; }
        public int MaximumLevel { get; set; }
        public int MonsterHealth { get; set; }
        public int MaxMonsterHealth { get; set; }
        public int MonsterDamage { get; set; }
        public int MaxMonsterDamage { get; set; }
        public int DropRate { get; set; }
        public int MaxDropRate { get; set; }
        public int ExperienceRate { get; set; }
        public int MaxExperienceRate { get; set; }
        public int GoldRate { get; set; }
        public int MaxGoldRate { get; set; }
        public int SkillDelay { get; set; }
    }

    public class MapRegionViewModel
    {
        public int RegionIndex { get; set; }
        public string Description { get; set; } = "";
        public int Size { get; set; }
    }

    public class RegionMonsterViewModel
    {
        public int RespawnIndex { get; set; }
        public int MonsterIndex { get; set; }
        public string MonsterName { get; set; } = "";
        public int MonsterLevel { get; set; }
        public int Delay { get; set; }
        public int Count { get; set; }
        public int DropSet { get; set; }
        public bool EventSpawn { get; set; }
        public bool Announce { get; set; }
        public int EasterEventChance { get; set; }
    }

    public class MonsterSelectOption
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int Image { get; set; }
    }
}
