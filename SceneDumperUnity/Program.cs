using SceneDumperUnity;

SceneDumperTools tmp = new SceneDumperTools();
var targetDir = args[0];
var outputDir = args[1];
tmp.DumpSceneData(targetDir, outputDir);