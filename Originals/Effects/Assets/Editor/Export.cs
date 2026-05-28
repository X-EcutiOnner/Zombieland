using UnityEditor;
using UnityEngine;
using System.IO;

public class CreateAssetBundles
{
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
	}

	public static void ListDeployedAssetBundles()
	{
		foreach (var arch in new[] { "Win64", "Linux", "MacOS" })
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
		foreach (var arch in new[] { "Win64", "Linux", "MacOS" })
		{
			var path = Path.Combine(DeploymentDir(), arch, "zombieland");
			var bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
				throw new System.Exception($"Could not load asset bundle {path}");

			var dust = bundle.LoadAsset<GameObject>("Dust");
			if (dust == null)
				throw new System.Exception($"Could not load Dust from {path}");

			var metaballs = bundle.LoadAsset<Shader>("Metaballs");
			if (metaballs == null)
				throw new System.Exception($"Could not load Metaballs from {path}");

			Debug.Log($"Zombieland bundle validated {arch}: Dust={dust.name}, Metaballs={metaballs.name}, Unity={Application.unityVersion}");
			bundle.Unload(false);
		}
	}


	static void Build(string arch, BuildTarget target)
	{
		var src = $"Assets/AssetBundles/{arch}";
		if (!Directory.Exists(src))
			Directory.CreateDirectory(src);

		BuildPipeline.BuildAssetBundles(src, BuildAssetBundleOptions.None, target);

		var dest = Path.Combine(DeploymentDir(), arch);
		if (!Directory.Exists(dest))
			Directory.CreateDirectory(dest);

		File.Copy(Path.Combine(src, "zombieland"), Path.Combine(dest, "zombieland"), true);
	}
}
