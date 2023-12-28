using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace SceneDumperUnity;

public class SceneDumperTools
{
    //List for keeping track of all scripts that are inside the scene files 
    private readonly ConcurrentBag<Script> _globalScriptsList = new ConcurrentBag<Script>();
    public void DumpSceneData(string projectDir, string outputDir)
    {
        //Create output directory
        Directory.CreateDirectory(outputDir);
        
        //find the Scene directory 
        string scenesDir = Path.Combine(projectDir, "Assets", "Scenes");
        if (Directory.Exists(scenesDir))
        {
            // Get all .unity files in the Scenes directory
            string[] unityFiles = Directory.GetFiles(scenesDir, "*.unity");
            List<Task> tasks = new List<Task>();
            foreach (var unityFile in unityFiles)
                tasks.Add(Task.Run(() => ParseAndCreateDumpFiles(unityFile, outputDir)));
            Task.WaitAll(tasks.ToArray());
        }
        else
            Console.WriteLine($"Scenes directory not found at {scenesDir}");
        
        //Checking Scripts use in scene
        string scriptsDir = Path.Combine(projectDir, "Assets", "Scripts");
        if (Directory.Exists(scriptsDir))
        {
            ParseAndCheckForScriptUseParallel(scriptsDir, outputDir);
        }
        else 
            Console.WriteLine($"Scripts directory not found at {scriptsDir}");
    }
    private void ParseAndCheckForScriptUseParallel(string targetDir, string outputDir)
    {
        string csvFilePath = Path.Combine(outputDir, "UnusedScripts.csv");
        string[] metaFiles = Directory.GetFiles(targetDir, "*.meta", SearchOption.AllDirectories); //searches recursively

        //ConcurrentBag for thread-safe collection
        ConcurrentBag<string> unusedScriptsLines = new ConcurrentBag<string>();
        Parallel.ForEach(metaFiles, metaFile =>
        {
            string currentGuid = ParseMetaFile(metaFile);

            if (!string.IsNullOrEmpty(currentGuid) && currentGuid != "notMono")
            {
                Script? existingScript = _globalScriptsList.FirstOrDefault(script => script.Guid == currentGuid);
                if (existingScript != null)
                {
                    string csFile = Path.Combine(Path.GetDirectoryName(metaFile), Path.GetFileNameWithoutExtension(metaFile));
                    bool hashResult = HashPublicVariables(csFile);
                    if (!hashResult)
                    {
                        string newLine = $"{currentGuid},{metaFile}";
                        unusedScriptsLines.Add(newLine);
                    }
                }
                else
                    unusedScriptsLines.Add($"{currentGuid},{metaFile}");
            }
        });
        AddLinesToCsvFile(csvFilePath, unusedScriptsLines.ToList());
    }
    private void ParseAndCreateDumpFiles(string targetFile, string fileDir)
    {
        //parsing the .unity file in parallel
        Task<List<GameObject>> getGameObjectsTask = Task.Run(() => GetGameObjectsAsync(targetFile));
        Task<List<Transform>> getTransformsTask = Task.Run(() => GetTransformsAsync(targetFile));
        Task<List<Script>> getScriptsTask = Task.Run(() => GetScriptsAsync(targetFile));

        // Wait for all tasks
        Task.WaitAll(getGameObjectsTask, getTransformsTask, getScriptsTask);

        // Assign results from tasks
        List<GameObject> gameObjects = getGameObjectsTask.Result;
        List<Transform> transformObjects = getTransformsTask.Result;
        List<Script> scripts = getScriptsTask.Result;
        
        foreach (var script in scripts)
            _globalScriptsList.Add(script);
        
        //creating new file and initializing print method
        using StreamWriter writer = new StreamWriter(Path.Combine(fileDir, Path.GetFileName(targetFile + ".dump")));
        foreach (var transform in transformObjects.Where(transform => transform.FatherId == "0"))
            PrintHierarchy(writer, gameObjects, transformObjects, transform);
    }
    private async Task<List<GameObject>> GetGameObjectsAsync(string dirPath)
    {
        List<GameObject> gameObjectsStructured = new List<GameObject>();
        List<List<string>> objectLists = GetObjectsById(dirPath, "1");
        
        //ConcurrentBag for thread-safe collection
        ConcurrentBag<GameObject> threadSafeGameObjects = new ConcurrentBag<GameObject>();
        Parallel.ForEach(objectLists, gameObjectString =>
        {
            string fileId = UseRegex(gameObjectString[0], @"(?<=&)\d+");
            gameObjectString.RemoveRange(0, 2);
            Dictionary<string, object> parsedObject = ParseLines(gameObjectString);

            string? m_name = parsedObject["m_Name"].ToString();
            string? components = parsedObject["m_Component"].ToString();

            threadSafeGameObjects.Add(new GameObject(m_name, fileId, components));
        });
        gameObjectsStructured.AddRange(threadSafeGameObjects);
        return gameObjectsStructured;
    }
    private async Task<List<Transform>> GetTransformsAsync(string dirPath)
    {
        List<Transform> transformsStructured = new List<Transform>();
        List<List<string>> objectLists = GetObjectsById(dirPath, "4");

        //ConcurrentBag for thread-safe collection
        ConcurrentBag<Transform> threadSafeTransforms = new ConcurrentBag<Transform>();

        Parallel.ForEach(objectLists, transformString =>
        {
            string fileId = UseRegex(transformString[0], @"(?<=&)\d+");
            transformString.RemoveRange(0, 2);
            Dictionary<string, object> parsedObj = ParseLines(transformString);

            string? childFileId = parsedObj["m_Children"].ToString();
            string? fatherFileId = parsedObj["m_Father"].ToString();
            string? gameObjId = parsedObj["m_GameObject"].ToString();

            threadSafeTransforms.Add(new Transform(fileId, gameObjId, childFileId, fatherFileId));
        });
        transformsStructured.AddRange(threadSafeTransforms);
        return transformsStructured;
    }
    private async Task<List<Script>> GetScriptsAsync(string dirPath)
    {
        List<Script> scriptsStructured = new List<Script>();
        List<List<string>> objectLists = GetObjectsById(dirPath, "114");

        //ConcurrentBag for thread-safe collection
        ConcurrentBag<Script> threadSafeScripts = new ConcurrentBag<Script>();
        Parallel.ForEach(objectLists, scriptString =>
        {
            scriptString.RemoveRange(0, 2);
            Dictionary<string, object> parsedScript = ParseLines(scriptString);

            string fileId = UseRegex(parsedScript["m_Script"].ToString(), @"(?<=fileID:\s*)(\d+)");
            string guid = UseRegex(parsedScript["m_Script"].ToString(), @"(?<=guid:\s*)([a-fA-F0-9]+)");
            string m_gameObject = parsedScript["m_GameObject"].ToString();

            threadSafeScripts.Add(new Script(fileId, guid, m_gameObject));
        });
        scriptsStructured.AddRange(threadSafeScripts);
        return scriptsStructured;
    }
    private Dictionary<string, object> ParseLines(List<string> lines)
    {
        Dictionary<string, object> keyValuePairs = new Dictionary<string, object>();

        foreach (var line in lines)
        {
            string[] parts = line.Split(':');

            if (parts.Length >= 2)
            {
                string key = parts[0].Trim();
                string value = string.Join(":", parts.Skip(1));

                switch (key) // filter for list of values 
                {
                    case "- component": //list of components in GameObject type
                        value = UseRegex(value, @"(?<=fileID:\s*)(\d+)");
                        keyValuePairs["m_Component"] += "-" + value;
                        break;
                    // for children in transform
                    case "- {fileID": // all Children of transform starts in this form
                        value = UseRegex(value, @"(\d+)(?=\})");
                        keyValuePairs["m_Children"] += "-" + value;
                        break;
                    case "m_Father":
                        value = UseRegex(value, @"(?<=fileID:\s*)(\d+)");
                        keyValuePairs.Add(key, value);
                        break;
                    case "m_GameObject":
                        value = UseRegex(value, @"(?<=fileID:\s*)(\d+)");
                        keyValuePairs.Add(key, value);
                        break;
                    default:
                        keyValuePairs.Add(key, value);
                        break;
                }
            }
        }

        return keyValuePairs;
    }
    private string ParseMetaFile(string file)
    {
        string[] lines = File.ReadAllLines(file);
        string guid = "0";
        bool isMonoFile = false;
        foreach (var line in lines)
        {
            if (line.Contains("guid:"))
                guid = UseRegex(line, @"(?<=guid:\s*)([a-fA-F0-9]+)");
            if (line.Contains("MonoImporter:"))
                isMonoFile = true;
        }
        return isMonoFile ? guid : "notMono";
    }
    private bool HashPublicVariables(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string code = File.ReadAllText(filePath);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();

                // Check if there are public or [SerializeField] variables in the class
                bool hasPublicVariables = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .SelectMany(classDeclaration => classDeclaration.Members
                        .OfType<FieldDeclarationSyntax>()
                        .Where(fieldDeclaration =>
                            fieldDeclaration.Modifiers.Any(SyntaxKind.PublicKeyword) ||
                            (fieldDeclaration.AttributeLists.Any(attributeList =>
                                attributeList.Attributes.Any(attribute =>
                                    attribute.Name.ToString() == "SerializeField"))))
                    )
                    .Any();
                return hasPublicVariables;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while analyzing C# file for fileId {filePath}: {ex.Message}");
        }

        return false;
    }
    private List<List<string>> GetObjectsById(string dirPath, string idNumber)
    {
        List<List<String>> gameObjects = new List<List<string>>();
        using StreamReader sr = new StreamReader(dirPath);
        
        bool objectFound = false;
        bool insideTheObject = false;
        List<string> yamlPart = new List<string>();
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
        
            if (line.Contains("--- !u!"+idNumber+" ") && !insideTheObject)
            {
                insideTheObject = true;
                objectFound = true;
                yamlPart.Add(line);
                continue;
            }
            if (objectFound && line.Contains("--- !u!"+idNumber+" ")) // checking for the case when there is same tag back to back 
            {
                gameObjects.Add(new List<string>(yamlPart));
                yamlPart.Clear();
            }else if (objectFound && line.Contains("--- "))
            {
                objectFound = false; // Reset the flag
                insideTheObject = false;
                gameObjects.Add(new List<string>(yamlPart));
                yamlPart.Clear();
                continue; // skip the loop once "---" is found
            }
            if (objectFound)
            {
                yamlPart.Add(line);
            }
        }
        return gameObjects;
    }
    private void PrintHierarchy(StreamWriter writer, List<GameObject> gameObjects, List<Transform> transforms, Transform currentTransform, string indent = "")
    {
        // Find the GameObject using GameObjectFileId
        var gameObject = gameObjects.Find(go => go.FileId == currentTransform.GameObjId);
        writer.WriteLine($"{indent}{gameObject?.m_Name}");

        if (currentTransform.ChildFileIds?.Trim() == "[]")
            return;
        string[]? childFileIds = currentTransform.ChildFileIds?.Split('-');

        if (childFileIds != null)
            foreach (var childFileId in childFileIds)
            {
                if (childFileId == "") continue;
                var childTransform = transforms.Find(t => t.FileId == childFileId);
                if (childTransform != null) PrintHierarchy(writer, gameObjects, transforms, childTransform, indent + "--");
            }
    } 
    //helper function for editing Strings using Regex 
    private string UseRegex(string? input, string regexRule)
    {
        if (input == null)
            return "NULL VALUE";
        Regex regex = new Regex(regexRule, RegexOptions.IgnoreCase);
        Match match = regex.Match(input);
        return match.Success ? match.Value : "REGEX_ERROR";
    }
    
    //helper function for adding to file
    //lock for the stream
    private static readonly object CsvFileLock = new object();
    private void AddLinesToCsvFile(string filePath, List<string> lines)
    {
        try
        {
            lock (CsvFileLock)
            {
                using StreamWriter sw = new StreamWriter(filePath, true);
                foreach (var line in lines)
                    sw.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while adding lines to CSV file: {ex.Message}");
        }
    }
}
public class GameObject
{
    public readonly string? m_Name;
    public readonly string FileId;
    public readonly string? ComponentFileIds;

    public GameObject(string? mName, string fileId, string? componentFileIds = null)
    {
        m_Name = mName;
        FileId = fileId;
        ComponentFileIds = componentFileIds;
    }
}
public class Transform
{
    public readonly string FileId;
    public readonly string? GameObjId;
    public readonly string? ChildFileIds;
    public readonly string? FatherId;

    public Transform(string fileId, string? gameObjId, string? childFileIds = null, string? fatherId = null)
    {
        FileId = fileId;
        this.ChildFileIds = childFileIds;
        this.FatherId = fatherId;
        this.GameObjId = gameObjId;
    }
}
public class Script
{
    public readonly string FileId;
    public readonly string Guid;
    public readonly string? GameObjectId;

    public Script(string fileId, string guid, string? gameObjectId)
    {
        FileId = fileId;
        this.Guid = guid;
        this.GameObjectId = gameObjectId;
    }
}