using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Server.Envir;
using Library.SystemModels;
using Library;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using Zircon.Server.Models;

namespace Server.Web.Pages
{
    [Authorize]
    public class MonstersModel : PageModel
    {
        public List<MonsterViewModel> Monsters { get; set; } = new();
        public int TotalCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Keyword { get; set; }

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        public int PageSize { get; set; } = 50;
        public int TotalPages => (TotalCount + PageSize - 1) / PageSize;

        public string? Message { get; set; }

        private bool IsAjaxRequest()
        {
            if (HttpContext?.Request?.Headers == null) return false;
            return HttpContext.Request.Headers.TryGetValue("X-Requested-With", out var values)
                   && values.Any(v => v.Equals("XMLHttpRequest", System.StringComparison.OrdinalIgnoreCase));
        }

        public void OnGet()
        {
            LoadMonsters();
        }

        private MonsterViewModel ToViewModel(MonsterInfo monster)
        {
            return new MonsterViewModel
            {
                Index = monster.Index,
                MonsterName = monster.MonsterName ?? "Unknown",
                Level = monster.Level,
                Experience = (long)monster.Experience,
                HP = monster.Stats?[Stat.Health] ?? 0,
                MinAC = monster.Stats?[Stat.MinAC] ?? 0,
                MaxAC = monster.Stats?[Stat.MaxAC] ?? 0,
                MinDC = monster.Stats?[Stat.MinDC] ?? 0,
                MaxDC = monster.Stats?[Stat.MaxDC] ?? 0,
                ViewRange = monster.ViewRange,
                IsBoss = monster.IsBoss
            };
        }

        private void LoadMonsters()
        {
            try
            {
                if (SEnvir.MonsterInfoList?.Binding == null) return;

                var query = SEnvir.MonsterInfoList.Binding.AsEnumerable();

                // Keyword filter
                if (!string.IsNullOrWhiteSpace(Keyword))
                {
                    query = query.Where(m =>
                        (m.MonsterName?.Contains(Keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        m.Index.ToString().Contains(Keyword));
                }

                TotalCount = query.Count();

                Monsters = query
                    .OrderBy(m => m.Level)
                    .ThenBy(m => m.Index)
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .Select(ToViewModel)
                    .ToList();
            }
            catch
            {
                // Prevent enumeration errors
            }
        }

        public IActionResult OnPostSpawnMonster(string playerName, int monsterIndex, int count, int range)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                var result = new { success = false, message = "权限不足，需要 Admin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }

            try
            {
                // Find the online player
                var player = SEnvir.Players.FirstOrDefault(p =>
                    p?.Character?.CharacterName?.Equals(playerName, System.StringComparison.OrdinalIgnoreCase) == true);

                if (player == null)
                {
                    var result = new { success = false, message = $"玩家 {playerName} 不在线" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                // Find the monster info
                var monsterInfo = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monsterInfo == null)
                {
                    var result = new { success = false, message = $"怪物索引 {monsterIndex} 不存在" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                // Validate parameters
                if (count < 1) count = 1;
                if (count > 100) count = 100;
                if (range < 1) range = 3;
                if (range > 20) range = 20;

                // Get player's current map and position
                var map = player.CurrentMap;
                if (map == null)
                {
                    var result = new { success = false, message = $"无法获取玩家 {playerName} 所在地图" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                var mapInfo = map.Info;
                if (mapInfo == null)
                {
                    var result = new { success = false, message = $"无法获取玩家 {playerName} 所在地图信息" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                int spawned = 0;
                var random = new System.Random();

                for (int i = 0; i < count; i++)
                {
                    // Calculate spawn position within range of player
                    int offsetX = random.Next(-range, range + 1);
                    int offsetY = random.Next(-range, range + 1);
                    int spawnX = player.CurrentLocation.X + offsetX;
                    int spawnY = player.CurrentLocation.Y + offsetY;

                    // Ensure spawn position is valid
                    if (spawnX < 0) spawnX = 0;
                    if (spawnY < 0) spawnY = 0;
                    if (spawnX >= map.Width) spawnX = map.Width - 1;
                    if (spawnY >= map.Height) spawnY = map.Height - 1;

                    var spawnPoint = new Point(spawnX, spawnY);

                    // Check if the cell is valid for spawning and find a valid location
                    bool found = false;
                    for (int attempt = 0; attempt < 20 && !found; attempt++)
                    {
                        var cell = map.GetCell(spawnPoint);
                        if (cell != null && cell.Movements == null)
                        {
                            found = true;
                            break;
                        }

                        // Try another random position
                        offsetX = random.Next(-range, range + 1);
                        offsetY = random.Next(-range, range + 1);
                        spawnX = player.CurrentLocation.X + offsetX;
                        spawnY = player.CurrentLocation.Y + offsetY;

                        if (spawnX >= 0 && spawnY >= 0 && spawnX < map.Width && spawnY < map.Height)
                        {
                            spawnPoint = new Point(spawnX, spawnY);
                        }
                    }

                    if (!found) continue;

                    // Create and spawn the monster
                    var monster = new MonsterObject
                    {
                        MonsterInfo = monsterInfo
                    };

                    if (monster.Spawn(mapInfo, spawnPoint))
                    {
                        spawned++;
                    }
                }

                SEnvir.Log($"[Admin] 召唤怪物: {monsterInfo.MonsterName} x{spawned} 在玩家 {playerName} 附近");
                var success = new { success = true, message = $"成功在玩家 {playerName} 附近召唤了 {spawned}/{count} 只 {monsterInfo.MonsterName}" };
                if (IsAjaxRequest()) return new JsonResult(success);

                Message = success.message;
                LoadMonsters();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"召唤失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }
        }

        public IActionResult OnPostKillMapMonsters(string playerName)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                var result = new { success = false, message = "权限不足，需要 Admin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }

            try
            {
                var player = SEnvir.Players.FirstOrDefault(p =>
                    p?.Character?.CharacterName?.Equals(playerName, System.StringComparison.OrdinalIgnoreCase) == true);

                if (player == null)
                {
                    var result = new { success = false, message = $"玩家 {playerName} 不在线" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                var map = player.CurrentMap;
                if (map == null)
                {
                    var result = new { success = false, message = $"无法获取玩家 {playerName} 所在地图" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                int killed = 0;
                var monsters = map.Objects?.OfType<MonsterObject>().ToList() ?? new List<MonsterObject>();

                foreach (var monster in monsters)
                {
                    if (monster != null && !monster.Dead)
                    {
                        monster.Die();
                        killed++;
                    }
                }

                SEnvir.Log($"[Admin] 清除地图怪物: {map.Info?.Description} 共 {killed} 只");
                var success = new { success = true, message = $"已清除玩家 {playerName} 所在地图的 {killed} 只怪物" };
                if (IsAjaxRequest()) return new JsonResult(success);

                Message = success.message;
                LoadMonsters();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"操作失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }
        }

        // 获取怪物详情 (AJAX)
        public IActionResult OnGetMonsterDetail(int monsterIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var monster = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monster == null)
                {
                    return new JsonResult(new { success = false, message = "怪物不存在" });
                }

                var detail = new MonsterDetailViewModel
                {
                    Index = monster.Index,
                    MonsterName = monster.MonsterName ?? "",
                    Level = monster.Level,
                    Experience = (long)monster.Experience,
                    ViewRange = monster.ViewRange,
                    CoolEye = monster.CoolEye,
                    AttackDelay = monster.AttackDelay,
                    MoveDelay = monster.MoveDelay,
                    IsBoss = monster.IsBoss,
                    Undead = monster.Undead,
                    CanPush = monster.CanPush,
                    CanTame = monster.CanTame,
                    // Stats
                    Health = monster.Stats?[Stat.Health] ?? 0,
                    MinAC = monster.Stats?[Stat.MinAC] ?? 0,
                    MaxAC = monster.Stats?[Stat.MaxAC] ?? 0,
                    MinMR = monster.Stats?[Stat.MinMR] ?? 0,
                    MaxMR = monster.Stats?[Stat.MaxMR] ?? 0,
                    MinDC = monster.Stats?[Stat.MinDC] ?? 0,
                    MaxDC = monster.Stats?[Stat.MaxDC] ?? 0,
                    MinMC = monster.Stats?[Stat.MinMC] ?? 0,
                    MaxMC = monster.Stats?[Stat.MaxMC] ?? 0,
                    MinSC = monster.Stats?[Stat.MinSC] ?? 0,
                    MaxSC = monster.Stats?[Stat.MaxSC] ?? 0,
                    Accuracy = monster.Stats?[Stat.Accuracy] ?? 0,
                    Agility = monster.Stats?[Stat.Agility] ?? 0
                };

                return new JsonResult(new { success = true, data = detail });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 新建怪物
        public IActionResult OnPostCreateMonster(
            string monsterName,
            int level,
            long experience,
            int viewRange,
            int coolEye,
            int attackDelay,
            int moveDelay,
            bool isBoss,
            bool undead,
            bool canPush,
            bool canTame,
            int health,
            int minAC,
            int maxAC,
            int minMR,
            int maxMR,
            int minDC,
            int maxDC,
            int minMC,
            int maxMC,
            int minSC,
            int maxSC,
            int accuracy,
            int agility)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                var result = new { success = false, message = "权限不足，需要 SuperAdmin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }

            try
            {
                if (string.IsNullOrWhiteSpace(monsterName))
                {
                    var result = new { success = false, message = "怪物名称不能为空" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                var newMonster = SEnvir.MonsterInfoList?.CreateNewObject();
                if (newMonster == null)
                {
                    var result = new { success = false, message = "创建怪物失败" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                // 设置基本属性
                newMonster.MonsterName = monsterName;
                newMonster.Level = level;
                newMonster.Experience = experience;
                newMonster.ViewRange = viewRange;
                newMonster.CoolEye = coolEye;
                newMonster.AttackDelay = attackDelay;
                newMonster.MoveDelay = moveDelay;
                newMonster.IsBoss = isBoss;
                newMonster.Undead = undead;
                newMonster.CanPush = canPush;
                newMonster.CanTame = canTame;

                // 创建 Stats
                CreateMonsterStat(newMonster, Stat.Health, health);
                CreateMonsterStat(newMonster, Stat.MinAC, minAC);
                CreateMonsterStat(newMonster, Stat.MaxAC, maxAC);
                CreateMonsterStat(newMonster, Stat.MinMR, minMR);
                CreateMonsterStat(newMonster, Stat.MaxMR, maxMR);
                CreateMonsterStat(newMonster, Stat.MinDC, minDC);
                CreateMonsterStat(newMonster, Stat.MaxDC, maxDC);
                CreateMonsterStat(newMonster, Stat.MinMC, minMC);
                CreateMonsterStat(newMonster, Stat.MaxMC, maxMC);
                CreateMonsterStat(newMonster, Stat.MinSC, minSC);
                CreateMonsterStat(newMonster, Stat.MaxSC, maxSC);
                CreateMonsterStat(newMonster, Stat.Accuracy, accuracy);
                CreateMonsterStat(newMonster, Stat.Agility, agility);

                // 重新计算 Stats
                newMonster.StatsChanged();

                SEnvir.Log($"[Admin] 新建怪物: [{newMonster.Index}] {monsterName}");
                var success = new
                {
                    success = true,
                    message = $"怪物 [{newMonster.Index}] {monsterName} 创建成功",
                    data = ToViewModel(newMonster)
                };
                if (IsAjaxRequest()) return new JsonResult(success);

                Message = success.message;
                LoadMonsters();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"创建失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }
        }

        private void CreateMonsterStat(MonsterInfo monster, Stat stat, int value)
        {
            if (value == 0) return;
            if (monster.MonsterInfoStats == null) return;

            var newStat = SEnvir.MonsterStatList?.CreateNewObject();
            if (newStat != null)
            {
                newStat.Stat = stat;
                newStat.Amount = value;
                newStat.Monster = monster;
                monster.MonsterInfoStats.Add(newStat);
            }
        }

        // 修改怪物属性
        public IActionResult OnPostUpdateMonster(
            int monsterIndex,
            string monsterName,
            int level,
            long experience,
            int viewRange,
            int coolEye,
            int attackDelay,
            int moveDelay,
            bool isBoss,
            bool undead,
            bool canPush,
            bool canTame,
            int health,
            int minAC,
            int maxAC,
            int minMR,
            int maxMR,
            int minDC,
            int maxDC,
            int minMC,
            int maxMC,
            int minSC,
            int maxSC,
            int accuracy,
            int agility)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                var result = new { success = false, message = "权限不足，需要 SuperAdmin 权限" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }

            try
            {
                var monster = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monster == null)
                {
                    var result = new { success = false, message = $"怪物索引 {monsterIndex} 不存在" };
                    if (IsAjaxRequest()) return new JsonResult(result);

                    Message = result.message;
                    LoadMonsters();
                    return Page();
                }

                // 记录旧值用于日志
                var oldName = monster.MonsterName;

                // 更新基本属性
                monster.MonsterName = monsterName;
                monster.Level = level;
                monster.Experience = experience;
                monster.ViewRange = viewRange;
                monster.CoolEye = coolEye;
                monster.AttackDelay = attackDelay;
                monster.MoveDelay = moveDelay;
                monster.IsBoss = isBoss;
                monster.Undead = undead;
                monster.CanPush = canPush;
                monster.CanTame = canTame;

                // 更新 Stats - 需要通过 MonsterInfoStats 修改
                UpdateMonsterStat(monster, Stat.Health, health);
                UpdateMonsterStat(monster, Stat.MinAC, minAC);
                UpdateMonsterStat(monster, Stat.MaxAC, maxAC);
                UpdateMonsterStat(monster, Stat.MinMR, minMR);
                UpdateMonsterStat(monster, Stat.MaxMR, maxMR);
                UpdateMonsterStat(monster, Stat.MinDC, minDC);
                UpdateMonsterStat(monster, Stat.MaxDC, maxDC);
                UpdateMonsterStat(monster, Stat.MinMC, minMC);
                UpdateMonsterStat(monster, Stat.MaxMC, maxMC);
                UpdateMonsterStat(monster, Stat.MinSC, minSC);
                UpdateMonsterStat(monster, Stat.MaxSC, maxSC);
                UpdateMonsterStat(monster, Stat.Accuracy, accuracy);
                UpdateMonsterStat(monster, Stat.Agility, agility);

                // 重新计算 Stats
                monster.StatsChanged();

                SEnvir.Log($"[Admin] 修改怪物属性: [{monsterIndex}] {oldName} -> {monsterName}");
                var success = new
                {
                    success = true,
                    message = $"怪物 [{monsterIndex}] {monsterName} 属性已更新",
                    data = ToViewModel(monster)
                };
                if (IsAjaxRequest()) return new JsonResult(success);

                Message = success.message;
                LoadMonsters();
                return Page();
            }
            catch (System.Exception ex)
            {
                var result = new { success = false, message = $"修改失败: {ex.Message}" };
                if (IsAjaxRequest()) return new JsonResult(result);

                Message = result.message;
                LoadMonsters();
                return Page();
            }
        }

        private void UpdateMonsterStat(MonsterInfo monster, Stat stat, int value)
        {
            if (monster.MonsterInfoStats == null) return;

            var existingStat = monster.MonsterInfoStats.FirstOrDefault(s => s.Stat == stat);
            if (existingStat != null)
            {
                if (value == 0)
                {
                    // 删除为0的属性
                    monster.MonsterInfoStats.Remove(existingStat);
                }
                else
                {
                    existingStat.Amount = value;
                }
            }
            else if (value != 0)
            {
                // 创建新属性
                var newStat = SEnvir.MonsterStatList?.CreateNewObject();
                if (newStat != null)
                {
                    newStat.Stat = stat;
                    newStat.Amount = value;
                    newStat.Monster = monster;
                    monster.MonsterInfoStats.Add(newStat);
                }
            }
        }

        // 获取怪物的掉落列表
        public IActionResult OnGetMonsterDrops(int monsterIndex)
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var monster = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monster == null)
                {
                    return new JsonResult(new { success = false, message = "怪物不存在" });
                }

                var drops = monster.Drops?.Select(d => new DropInfoViewModel
                {
                    DropIndex = d.Index,
                    MonsterIndex = monster.Index,
                    ItemIndex = d.Item?.Index ?? 0,
                    ItemName = d.Item?.ItemName ?? "Unknown",
                    Chance = d.Chance,
                    Amount = d.Amount,
                    DropSet = d.DropSet,
                    PartOnly = d.PartOnly,
                    EasterEvent = d.EasterEvent
                }).ToList() ?? new List<DropInfoViewModel>();

                return new JsonResult(new { success = true, data = drops });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 获取所有物品列表（用于下拉选择）
        public IActionResult OnGetItemsList(string? keyword = "")
        {
            if (!HasPermission(AccountIdentity.Admin))
            {
                return new JsonResult(new { success = false, message = "权限不足" });
            }

            try
            {
                var items = SEnvir.ItemInfoList?.Binding?.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    items = items.Where(i =>
                        (i.ItemName?.Contains(keyword, System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                        i.Index.ToString().Contains(keyword));
                }

                var itemOptions = items?.OrderBy(i => i.Index).Take(100).Select(i => new ItemSelectOption
                {
                    Index = i.Index,
                    Name = i.ItemName ?? "Unknown",
                    ItemType = i.ItemType.ToString()
                }).ToList() ?? new List<ItemSelectOption>();

                return new JsonResult(new { success = true, data = itemOptions });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // 添加掉落物品
        public IActionResult OnPostAddDrop(int monsterIndex, int itemIndex, int chance, int amount, int dropSet, bool partOnly, bool easterEvent)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var monster = SEnvir.MonsterInfoList?.Binding?.FirstOrDefault(m => m.Index == monsterIndex);
                if (monster == null)
                {
                    return new JsonResult(new { success = false, message = "怪物不存在" });
                }

                var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(i => i.Index == itemIndex);
                if (item == null)
                {
                    return new JsonResult(new { success = false, message = $"物品索引 {itemIndex} 不存在" });
                }

                var newDrop = SEnvir.DropInfoList?.CreateNewObject();
                if (newDrop == null)
                {
                    return new JsonResult(new { success = false, message = "创建掉落记录失败" });
                }

                // 设置掉落属性
                newDrop.Monster = monster;
                newDrop.Item = item;
                newDrop.Chance = System.Math.Max(1, System.Math.Min(10000, chance));
                newDrop.Amount = System.Math.Max(1, amount);
                newDrop.DropSet = dropSet;
                newDrop.PartOnly = partOnly;
                newDrop.EasterEvent = easterEvent;

                // 添加到怪物的掉落列表
                monster.Drops?.Add(newDrop);

                SEnvir.Log($"[Admin] 添加掉落: 怪物[{monsterIndex}] {monster.MonsterName} -> 物品[{itemIndex}] {item.ItemName}, 几率={chance}");
                
                var dropViewModel = new DropInfoViewModel
                {
                    DropIndex = newDrop.Index,
                    MonsterIndex = monster.Index,
                    ItemIndex = item.Index,
                    ItemName = item.ItemName ?? "",
                    Chance = newDrop.Chance,
                    Amount = newDrop.Amount,
                    DropSet = newDrop.DropSet,
                    PartOnly = newDrop.PartOnly,
                    EasterEvent = newDrop.EasterEvent
                };

                return new JsonResult(new { success = true, message = "掉落添加成功", data = dropViewModel });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"添加失败: {ex.Message}" });
            }
        }

        // 更新掉落
        public IActionResult OnPostUpdateDrop(int dropIndex, int chance, int amount, int dropSet, bool partOnly, bool easterEvent)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var drop = SEnvir.DropInfoList?.Binding?.FirstOrDefault(d => d.Index == dropIndex);
                if (drop == null)
                {
                    return new JsonResult(new { success = false, message = "掉落记录不存在" });
                }

                // 更新属性
                drop.Chance = System.Math.Max(1, System.Math.Min(10000, chance));
                drop.Amount = System.Math.Max(1, amount);
                drop.DropSet = dropSet;
                drop.PartOnly = partOnly;
                drop.EasterEvent = easterEvent;

                SEnvir.Log($"[Admin] 更新掉落: 掉落[{dropIndex}] 物品[{drop.Item?.Index}] {drop.Item?.ItemName}");
                
                var dropViewModel = new DropInfoViewModel
                {
                    DropIndex = drop.Index,
                    MonsterIndex = drop.Monster?.Index ?? 0,
                    ItemIndex = drop.Item?.Index ?? 0,
                    ItemName = drop.Item?.ItemName ?? "",
                    Chance = drop.Chance,
                    Amount = drop.Amount,
                    DropSet = drop.DropSet,
                    PartOnly = drop.PartOnly,
                    EasterEvent = drop.EasterEvent
                };

                return new JsonResult(new { success = true, message = "掉落已更新", data = dropViewModel });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"更新失败: {ex.Message}" });
            }
        }

        // 删除掉落
        public IActionResult OnPostDeleteDrop(int dropIndex)
        {
            if (!HasPermission(AccountIdentity.SuperAdmin))
            {
                return new JsonResult(new { success = false, message = "权限不足，需要 SuperAdmin 权限" });
            }

            try
            {
                var drop = SEnvir.DropInfoList?.Binding?.FirstOrDefault(d => d.Index == dropIndex);
                if (drop == null)
                {
                    return new JsonResult(new { success = false, message = "掉落记录不存在" });
                }

                var monsterIndex = drop.Monster?.Index ?? 0;
                var itemName = drop.Item?.ItemName ?? "";

                // 从怪物的掉落列表中移除
                if (drop.Monster?.Drops != null)
                {
                    drop.Monster.Drops.Remove(drop);
                }

                // 删除掉落记录
                drop.Delete();

                SEnvir.Log($"[Admin] 删除掉落: 怪物[{monsterIndex}] 掉落[{dropIndex}] 物品 {itemName}");
                return new JsonResult(new { success = true, message = "掉落已删除" });
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = $"删除失败: {ex.Message}" });
            }
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
    }

    public class MonsterViewModel
    {
        public int Index { get; set; }
        public string MonsterName { get; set; } = "";
        public int Level { get; set; }
        public long Experience { get; set; }
        public int HP { get; set; }
        public int MinAC { get; set; }
        public int MaxAC { get; set; }
        public int MinDC { get; set; }
        public int MaxDC { get; set; }
        public int ViewRange { get; set; }
        public bool IsBoss { get; set; }
    }

    public class MonsterDetailViewModel
    {
        public int Index { get; set; }
        public string MonsterName { get; set; } = "";
        public int Level { get; set; }
        public long Experience { get; set; }
        public int ViewRange { get; set; }
        public int CoolEye { get; set; }
        public int AttackDelay { get; set; }
        public int MoveDelay { get; set; }
        public bool IsBoss { get; set; }
        public bool Undead { get; set; }
        public bool CanPush { get; set; }
        public bool CanTame { get; set; }
        // Stats
        public int Health { get; set; }
        public int MinAC { get; set; }
        public int MaxAC { get; set; }
        public int MinMR { get; set; }
        public int MaxMR { get; set; }
        public int MinDC { get; set; }
        public int MaxDC { get; set; }
        public int MinMC { get; set; }
        public int MaxMC { get; set; }
        public int MinSC { get; set; }
        public int MaxSC { get; set; }
        public int Accuracy { get; set; }
        public int Agility { get; set; }
    }

    public class DropInfoViewModel
    {
        public int DropIndex { get; set; }
        public int MonsterIndex { get; set; }
        public int ItemIndex { get; set; }
        public string ItemName { get; set; } = "";
        public int Chance { get; set; }
        public int Amount { get; set; }
        public int DropSet { get; set; }
        public bool PartOnly { get; set; }
        public bool EasterEvent { get; set; }
    }

    public class ItemSelectOption
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string ItemType { get; set; } = "";
    }
}
