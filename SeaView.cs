using System.Text;
using HarmonyLib;
using AnchorChain;
using Environment = System.Environment;

namespace SeaView;

[ACPlugin("io.github.justoneother.sea_view_acmi", "SeaView", "0.1.0")]
public class SeaView : IAnchorChainMod
{
	private static StreamWriter _acmiWriter = null;
	private static float _accumulatedDTime = 0f;

	private static readonly HashSet<int> KnownObjects = new HashSet<int>();
	private static readonly List<int> ToDestroy = new List<int>();

	private static UnityEngine.InputSystem.InputAction _coerceModelAction = null;
	private static bool _coerceModel = false;


	private static readonly HashSet<SeaPower.ObjectBase.ObjectType> IgnoredTypes = new()
	{
		SeaPower.ObjectBase.ObjectType.Wakebubble,
		SeaPower.ObjectBase.ObjectType.AI_Marker
	};
	private static readonly Dictionary<SeaPower.ObjectBase.ObjectType, string> ObjectTypes = new()
	{
		{SeaPower.ObjectBase.ObjectType.Aircraft, "Air+FixedWing"},
		{SeaPower.ObjectBase.ObjectType.Biologic, "Sea+Biologic"},
		{SeaPower.ObjectBase.ObjectType.Bomb, "Weapon+Bomb"},
		{SeaPower.ObjectBase.ObjectType.Chaff, "Weapon+Decoy+Chaff"},
		{SeaPower.ObjectBase.ObjectType.Helicopter, "Air+Rotorcraft"},
		{SeaPower.ObjectBase.ObjectType.Decoy, "Weapon+Decoy"},
		{SeaPower.ObjectBase.ObjectType.Missile, "Weapon+Missile"},
		{SeaPower.ObjectBase.ObjectType.Noisemaker, "Weapon+Decoy"},
		{SeaPower.ObjectBase.ObjectType.Projectile, "Weapon+Projectile"},
		{SeaPower.ObjectBase.ObjectType.Submarine, "Sea+Submarine"},
		{SeaPower.ObjectBase.ObjectType.Torpedo, "Weapon+Torpedo"},
		{SeaPower.ObjectBase.ObjectType.AerialRocket, "Weapon+Rocket"},
		{SeaPower.ObjectBase.ObjectType.LandUnit, "Ground"},
		{SeaPower.ObjectBase.ObjectType.RBU, "Weapon+Rocket"},
		{SeaPower.ObjectBase.ObjectType.ASROC, "Weapon+Rocket"},
		{SeaPower.ObjectBase.ObjectType.Vessel, "Sea+Watercraft"},
		{SeaPower.ObjectBase.ObjectType.Any, ""},
		{SeaPower.ObjectBase.ObjectType.Other, ""},
	};
	private static readonly Dictionary<SeaPower.Taskforce.TfType, string> SideColor = new()
	{
		{SeaPower.Taskforce.TfType.None, "Green"}, {SeaPower.Taskforce.TfType.Player, "Blue"},
		{SeaPower.Taskforce.TfType.Ally, "Cyan"}, {SeaPower.Taskforce.TfType.Enemy, "Red"},
		{SeaPower.Taskforce.TfType.Neutral, "Green"}
	};

	private static readonly Dictionary<SeaPower.ObjectBaseParameters.UnitRoles, string> UnitModelsByRole = new()
	{
		{SeaPower.ObjectBaseParameters.UnitRoles.Deco, "Core.Cube.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SeaMine, "Core.Sphere.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SmallCivilian, "Watercraft.Cargo.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Merchant, "Watercraft.Cargo.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Passenger, "Watercraft.Cargo.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Transport, "Watercraft.Cargo.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Landing, "Watercraft.LST-1.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Minesweep, "Watercraft.Frigate.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.RAS, "Watercraft.Cargo.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Spy, "Watercraft.Zodiac.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Carrier, "Watercraft.CVN-59.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.CVS, "Watercraft.CVN-59.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SS, "Watercraft.Type VIIC.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SSN, "Watercraft.Project 971U.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SSG, "Watercraft.Project 877.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SSGN, "Watercraft.Ohio.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SSB, "Watercraft.Project 877.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SSBN, "Watercraft.Ohio.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Airliner, "FixedWing.B707-300B.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Fighter, "FixedWing.F-5.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Bomber, "FixedWing.A-7.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.HeavyBomber, "FixedWing.B-52.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Recon, "FixedWing.U-2.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.AEW, "FixedWing.E-2C.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.MPA, "FixedWing.P-3.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.SEAD, "FixedWing.F-4.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.Attack, "FixedWing.A-4.obj"},
		{SeaPower.ObjectBaseParameters.UnitRoles.LifeRaft, "Watercraft.Zodiac.obj"},
	};

	private static Dictionary<string, string> _unitModelsByClass = new();


	public static void AddModelByClass(string className, string model)
	{
		_unitModelsByClass[className] = model;
	}

	public static bool RemoveModelByClass(string className)
	{
		return _unitModelsByClass.Remove(className);
	}


	public void TriggerEntryPoint()
	{
		// Plugin startup logic
		var harmony = new Harmony("org.avesdev.plugins.sea_view_acmi");

		var disableAcmi = typeof(SeaView).GetMethod(nameof(SeaView.EndAcmi));
		var enableAcmi = typeof(SeaView).GetMethod(nameof(SeaView.BeginAcmi));
		var update = typeof(SeaView).GetMethod(nameof(SeaView.OnUpdate));
		var destroy = typeof(SeaView).GetMethod(nameof(SeaView.DestroyAcmiObject));
		var disableAcmiLS = typeof(SeaView).GetMethod(nameof(SeaView.EndAcmiLoadScreen));

		harmony.Patch(typeof(SeaPower.MissionManager).GetMethod(nameof(SeaPower.MissionManager.ExitToMainMenu)), prefix: new HarmonyMethod(disableAcmi));
		harmony.Patch(typeof(SeaPower.MissionManager).GetMethod(nameof(SeaPower.MissionManager.UnloadMissionAndReload)), prefix: new HarmonyMethod(disableAcmi), postfix: new HarmonyMethod(enableAcmi));
		harmony.Patch(typeof(SeaPower.GameInitializer).GetMethod(nameof(SeaPower.GameInitializer.init)), postfix: new HarmonyMethod(enableAcmi));
		harmony.Patch(typeof(SeaPower.GameUpdater).GetMethod(nameof(SeaPower.GameUpdater.update)), postfix: new HarmonyMethod(update));
		harmony.Patch(typeof(SeaPower.ObjectsManager).GetMethod(nameof(SeaPower.ObjectsManager.removeObject)), prefix: new HarmonyMethod(destroy));
		// Temp method until I can find a better way to identify closing the mission
		harmony.Patch(typeof(SeaPower.LoadScreen).GetMethod(nameof(SeaPower.LoadScreen.ExecuteLoadScreen)), prefix: new HarmonyMethod(disableAcmiLS));
	}


	public static void OnUpdate()
	{
		if (_coerceModelAction.triggered)
		{
			_coerceModel = !_coerceModel;
		}


		// Should work as a truth value for if player is in mission (unless I've missed an exit case)
		if (_acmiWriter is null)
		{
			return;
		}

		// Handle short pauses
		_accumulatedDTime += SeaPower.GameTime.deltaTime;
		if (_accumulatedDTime < 0.05f) { return; }
		_accumulatedDTime = 0f;

		_acmiWriter.WriteLine($"#{SeaPower.GameTime.time}");

		// We know that all Singleton<T>s are instantiated
		List<SeaPower.ObjectBase> objects = SeaPower.Singleton<SeaPower.ObjectsManager>.Instance._listOfAllObjects;

		foreach (var obj in objects)
		{
			if (IgnoredTypes.Contains(obj._type) || obj.IsDestroyed) { continue; }

			_acmiWriter.Write($"{obj.UniqueID:x8},");

			if (!KnownObjects.Contains(obj.UniqueID))
			{
				KnownObjects.Add(obj.UniqueID);
				_acmiWriter.Write($"Name={obj.ClassName},Type={ObjectTypes[obj._type]},Color={SideColor[obj._taskforce.Side]},Callsign={obj.getName()},");

				// Try to write a shape if any is present, else allow tacview to decide
				try
				{
					_acmiWriter.WriteLine($"Shape={_unitModelsByClass[obj.ClassName]},");
				}
				catch
				{
					if (_coerceModel)
					{
						try
						{
							_acmiWriter.WriteLine($"Shape={UnitModelsByRole[obj._obp._unitRoles[0]]},");
						} catch { }
					}
				}
			}

			SeaPower.GeoPosition objPos = obj.getGeoPosition();
			_acmiWriter.WriteLine($"T={objPos._longitude}|{objPos._latitude}|{obj.Altitude.Value}|0|0|{obj.getHeading()}");
		}

		if (ToDestroy.Count > 0)
		{
			foreach (var id in ToDestroy)
			{
				_acmiWriter.WriteLine($"{id:x8},Health=0,Visible=0");
			}

			_acmiWriter.Write("0,Event=Destroyed");
			foreach (int id in ToDestroy)
			{
				_acmiWriter.Write($"|{id:x8}");
				KnownObjects.Remove(id);  // We shouldn't need it again so free the memory
			}
			_acmiWriter.Write("\n");
			ToDestroy.Clear();
		}

		// Write all operations to disk
		_acmiWriter.Flush();
	}


	public static void EndAcmi()
	{

		if (_acmiWriter is not null)
		{
			_acmiWriter.Close();
			_acmiWriter.Dispose();
			_acmiWriter = null;
		}

	}


	public static void EndAcmiLoadScreen(List<SeaPower.LoadAction> coroutines)
	{
		if (coroutines.Count != 1 || coroutines[0].description != "UnloadMission")
		{
			return;
		}

		if (_acmiWriter is not null)
		{
			_acmiWriter.Close();
			_acmiWriter.Dispose();
			_acmiWriter = null;
		}
	}


	public static void BeginAcmi()
	{
		SeaPower.Environment timeData = SeaPower.Singleton<SeaPower.Environment>.Instance;

		if (_acmiWriter is not null)
		{
			EndAcmi();
		}

		_acmiWriter = new StreamWriter($@"{Environment.GetEnvironmentVariable("USERPROFILE")}\Saved Games\Sea View\{DateTime.Now:yy-MM-dd HH-mm-ss zz}.acmi", false, Encoding.UTF8);
		_acmiWriter.Write($"FileType=text/acmi/tacview\nFileVersion=2.2\n0,ReferenceTime={timeData.Year:D4}-{timeData.Month:D2}-{timeData.Day:D2}T{timeData.Hour:D2}:{timeData.Minutes:D2}:{(int) timeData.Seconds:D2}Z\n");
		_acmiWriter.Flush();

		// Clear any residuals
		ToDestroy.Clear();
	}


	public static void DestroyAcmiObject(SeaPower.ObjectBase obj)
	{
		// Don't destroy things that aren't displayed or are already dead
		if (!KnownObjects.Contains(obj.UniqueID) || ToDestroy.Contains(obj.UniqueID)) { return; }
		ToDestroy.Add(obj.UniqueID);
	}
}