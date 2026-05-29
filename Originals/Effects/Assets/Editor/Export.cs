using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class CreateAssetBundles
{
	static readonly string[] Architectures = { "Win64", "Linux", "MacOS" };
	static readonly string[] ExpectedAssetNames =
	{
		"assets/_zombieland/dust.prefab",
		"assets/_zombieland/metaballs.shader",
		"assets/_zombieland/smoke_n.png",
		"assets/_zombieland/smoke_thin.mat",
		"assets/_zombieland/smoke_thin.png",
		"assets/_zombieland/zombieblob.mat",
		"assets/_zombieland/zombieblob.shader"
	};

	static string DeploymentDir()
	{
		var env = System.Environment.GetEnvironmentVariable("ZOMBIELAND_RESOURCES_DIR");
		if (string.IsNullOrEmpty(env) == false)
			return env;

		return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Resources"));
	}

	[MenuItem("Assets/Export Zombieland")]
	public static void BuildStandaloneAssetBundles()
	{
		Build("Win64", BuildTarget.StandaloneWindows64);
		Build("Linux", BuildTarget.StandaloneLinux64);
		Build("MacOS", BuildTarget.StandaloneOSX);
		ValidateDeployedAssetBundles();
	}

	public static void ListDeployedAssetBundles()
	{
		foreach (var arch in Architectures)
		{
			var path = Path.Combine(DeploymentDir(), arch, "zombieland");
			Debug.Log($"Zombieland bundle {arch}: {path}");
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
			{
				Debug.LogError($"Could not load asset bundle {path}");
				continue;
			}
			foreach (var name in bundle.GetAllAssetNames())
				Debug.Log($"Zombieland bundle asset {arch}: {name}");
			bundle.Unload(false);
		}
	}

	public static void ValidateDeployedAssetBundles()
	{
		foreach (var arch in Architectures)
		{
			var path = Path.Combine(DeploymentDir(), arch, "zombieland");
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
				throw new System.Exception($"Could not load asset bundle {path}");

			ValidateBundle(arch, path, bundle);
			bundle.Unload(false);
		}
	}

	static void ValidateBundle(string arch, string path, AssetBundle bundle)
	{
		var actualNames = new HashSet<string>(bundle.GetAllAssetNames());
		var missingNames = ExpectedAssetNames.Where(name => actualNames.Contains(name) == false).ToArray();
		if (missingNames.Length > 0)
			throw new Exception($"Zombieland bundle {arch} is missing assets: {string.Join(", ", missingNames)}");

		var dust = RequireAsset<GameObject>(bundle, arch, ExpectedAssetNames[0]);
		var metaballs = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[1]);
		_ = RequireAsset<Texture2D>(bundle, arch, ExpectedAssetNames[2]);
		_ = RequireAsset<Material>(bundle, arch, ExpectedAssetNames[3]);
		_ = RequireAsset<Texture2D>(bundle, arch, ExpectedAssetNames[4]);
		_ = RequireAsset<Material>(bundle, arch, ExpectedAssetNames[5]);
		var zombieBlob = RequireAsset<Shader>(bundle, arch, ExpectedAssetNames[6]);

		Debug.Log($"Zombieland bundle validated {arch}: Dust={dust.name}, Metaballs={metaballs.name}, ZombieBlob={zombieBlob.name}, assets={actualNames.Count}, Unity={Application.unityVersion}, path={path}");
	}

	static T RequireAsset<T>(AssetBundle bundle, string arch, string assetName) where T : UnityEngine.Object
	{
		var asset = bundle.LoadAsset<T>(assetName);
		if (asset == null)
			throw new Exception($"Zombieland bundle {arch} could not load {assetName} as {typeof(T).Name}");
		return asset;
	}

	static void Build(string arch, BuildTarget target)
	{
		var src = $"Assets/AssetBundles/{arch}";
		if (!Directory.Exists(src))
			Directory.CreateDirectory(src);

		BuildPipeline.BuildAssetBundles(src, BuildAssetBundleOptions.None, target);

		var bundlePath = Path.Combine(src, "zombieland");
		if (File.Exists(bundlePath) == false)
			throw new Exception($"Unity did not produce {bundlePath}. No source asset is currently labelled for the zombieland asset bundle.");

		var dest = Path.Combine(DeploymentDir(), arch);
		if (!Directory.Exists(dest))
			Directory.CreateDirectory(dest);

		File.Copy(bundlePath, Path.Combine(dest, "zombieland"), true);
	}
}
