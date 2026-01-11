using System;
using System.IO;
using System.Diagnostics;
using Figgle;
using Figgle.Fonts;
using NAudio.Wave;
using NAudio.MediaFoundation;
using NAudio.Dsp;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace XTLZ_Piano;

internal class Program
{
    // 使用并发字典来管理多个播放器，允许多个音符同时播放
    private static readonly ConcurrentDictionary<int, WaveOutEvent> activePlayers = new();
    // 用于自动演奏的取消令牌
    private static CancellationTokenSource? autoPlayCancellation;
    
    // 调试日志和进程管理
    private static string? debugLogFilePath;
    private static Process? debugConsoleProcess;
    private static readonly object debugLogLock = new object();
    
    // 拟真钢琴键盘映射：键盘键 -> 音符编号（0=C3, 1=C#3, ..., 35=B5）
    private static readonly Dictionary<ConsoleKey, int> pianoKeyMapping = new()
    {
        // 低音区八度（C3-B3）
        { ConsoleKey.Z, 0 },  // C3
        { ConsoleKey.A, 1 },  // C#3
        { ConsoleKey.X, 2 },  // D3
        { ConsoleKey.S, 3 },  // D#3
        { ConsoleKey.C, 4 },  // E3
        { ConsoleKey.V, 5 },  // F3
        { ConsoleKey.D, 6 },  // F#3
        { ConsoleKey.B, 7 },  // G3
        { ConsoleKey.F, 8 },  // G#3
        { ConsoleKey.N, 9 },  // A3
        { ConsoleKey.G, 10 }, // A#3
        { ConsoleKey.M, 11 }, // B3
        
        // 中音区八度（C4-B4，中央C）
        { ConsoleKey.H, 12 }, // C4
        { ConsoleKey.R, 13 }, // C#4
        { ConsoleKey.J, 14 }, // D4
        { ConsoleKey.T, 15 }, // D#4
        { ConsoleKey.K, 16 }, // E4
        { ConsoleKey.L, 17 }, // F4
        { ConsoleKey.Y, 18 }, // F#4
        { ConsoleKey.Q, 19 }, // G4
        { ConsoleKey.U, 20 }, // G#4
        { ConsoleKey.W, 21 }, // A4
        { ConsoleKey.I, 22 }, // A#4
        { ConsoleKey.E, 23 }, // B4
        
        // 高音区八度（C5-B5）
        { ConsoleKey.O, 24 }, // C5
        { ConsoleKey.D6, 25 }, // C#5 (数字键6)
        { ConsoleKey.P, 26 }, // D5
        { ConsoleKey.D7, 27 }, // D#5 (数字键7)
        { ConsoleKey.D1, 28 }, // E5 (数字键1)
        { ConsoleKey.D2, 29 }, // F5 (数字键2)
        { ConsoleKey.D8, 30 }, // F#5 (数字键8)
        { ConsoleKey.D3, 31 }, // G5 (数字键3)
        { ConsoleKey.D9, 32 }, // G#5 (数字键9)
        { ConsoleKey.D4, 33 }, // A5 (数字键4)
        { ConsoleKey.D0, 34 }, // A#5 (数字键0)
        { ConsoleKey.D5, 35 }, // B5 (数字键5)
    };
    
    // 基准音文件（XTLZ-O.mp3）对应的音符编号（假设为C4）
    private const int BaseNoteNumber = 12; // C4
    
    // 延音踏板状态
    private static bool sustainPedalPressed = false;

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        // 检查命令行参数
        if (args.Length > 0 && (args[0] == "--generate-notes" || args[0] == "-g"))
        {
            Console.WriteLine("=== 预生成变调音符文件 ===");
            EnsurePitchShiftedNotesGenerated();
            Console.WriteLine("\n音符文件生成完成。");
            return;
        }
        
        // 显示艺术字体标题
        Console.WriteLine(FiggleFonts.Slant.Render("lsfStudio"));
        Console.WriteLine("项目名称：雪田梨子吟唱器");
        Console.WriteLine();

        // 预生成变调音符文件（如果需要）
        Console.WriteLine("检查音符文件...");
        EnsurePitchShiftedNotesGenerated();
        Console.WriteLine("准备就绪！\n");

        // 主菜单循环
        bool exitProgram = false;
        while (!exitProgram)
        {
            ShowMainMenu();
            string? input = Console.ReadLine()?.Trim();
            switch (input)
            {
                case "1":
                    PlayMode();
                    break;
                case "2":
                    RealisticPianoMode();
                    break;
                case "3":
                    PlayFromNotationFile();
                    break;
                case "4":
                    exitProgram = true;
                    break;
                default:
                    Console.WriteLine("无效输入，请重新选择。");
                    break;
            }
        }

        Console.WriteLine("感谢使用雪田梨子吟唱器，再见！");
    }

    static void ShowMainMenu()
    {
        Console.WriteLine("========== 主菜单 ==========");
        Console.WriteLine("1. 开始演奏（数字键1-7）");
        Console.WriteLine("2. 拟真钢琴键盘演奏");
        Console.WriteLine("3. 读取简谱文件并自动演奏");
        Console.WriteLine("4. 退出程序");
        Console.Write("请选择（输入数字）: ");
    }

    static void PlayMode()
    {
        // 启动调试控制台
        StartDebugConsole();
        Console.WriteLine("\n进入钢琴演奏模式。按数字键1~7播放对应音阶，按Esc键返回主菜单。");
        Console.WriteLine("提示：可以同时按下多个键演奏和弦，每个音符独立播放。");

        bool exitPlayMode = false;
        while (!exitPlayMode)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                exitPlayMode = true;
                StopAllPlayers();
                Console.WriteLine("\n退出演奏模式。");
            }
            else if (key.KeyChar >= '1' && key.KeyChar <= '7')
            {
                int noteIndex = key.KeyChar - '0'; // 转换字符'1'~'7'为数字1~7
                PlayPianoNote(noteIndex);
            }
            else
            {
                Console.WriteLine($"\n无效按键 '{key.KeyChar}'，请按1~7或按Esc键退出。");
            }
        }
        
        // 停止调试控制台
        StopDebugConsole();
    }

    static void PlayPianoNote(int note)
    {
        // 检查是否已经有这个音符在播放
        if (activePlayers.ContainsKey(note))
        {
            // 如果已经在播放，先停止它（模拟钢琴键重新按下）
            if (activePlayers.TryRemove(note, out var existingPlayer))
            {
                existingPlayer.Stop();
                existingPlayer.Dispose();
            }
        }

        string fileName = $"XTLZ-{note}.mp3";
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"\n文件 {fileName} 不存在，请确保文件位于程序目录。");
            return;
        }

        try
        {
            var audioFile = new AudioFileReader(filePath);
            var player = new WaveOutEvent();
            player.Init(audioFile);
            
            // 添加到活跃播放器字典
            activePlayers[note] = player;
            
            // 调试信息
            string debugMessage = $"[调试] 音符: {note}, 文件: {fileName}";
            WriteDebugLog(debugMessage);
            
            // 播放完成事件处理
            player.PlaybackStopped += (sender, e) =>
            {
                audioFile.Dispose();
                player.Dispose();
                
                // 从字典中移除（如果还存在）
                activePlayers.TryRemove(note, out _);
            };
            
            player.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n播放音符 {note} 时出错: {ex.Message}");
        }
    }

    static void StopAllPlayers()
    {
        // 停止所有活跃的播放器
        foreach (var kvp in activePlayers)
        {
            kvp.Value.Stop();
            kvp.Value.Dispose();
        }
        activePlayers.Clear();
    }

    static void PlayFromNotationFile()
    {
        Console.Write("\n请输入简谱文件路径（例如：D:\\1.txt）: ");
        string? filePath = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrEmpty(filePath))
        {
            Console.WriteLine("文件路径不能为空。");
            return;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"文件不存在: {filePath}");
            return;
        }

        try
        {
            string content = File.ReadAllText(filePath).Trim();
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine("文件内容为空。");
                return;
            }

            Console.WriteLine($"读取到简谱: {content}");
            Console.WriteLine("开始自动演奏...（按任意键停止）");
            
            // 创建取消令牌
            autoPlayCancellation = new CancellationTokenSource();
            var token = autoPlayCancellation.Token;
            
            // 启动自动演奏任务
            Task.Run(() => AutoPlayNotation(content, token), token);
            
            // 等待用户按键停止
            Console.ReadKey(intercept: true);
            
            // 取消自动演奏
            autoPlayCancellation.Cancel();
            autoPlayCancellation.Dispose();
            autoPlayCancellation = null;
            
            // 停止所有播放器
            StopAllPlayers();
            Console.WriteLine("\n自动演奏已停止。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取或播放简谱时出错: {ex.Message}");
        }
    }

    static async Task AutoPlayNotation(string notation, CancellationToken cancellationToken)
    {
        const int noteDuration = 300; // 每个音符播放时长（毫秒）
        
        foreach (char c in notation)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            if (c >= '1' && c <= '7')
            {
                int note = c - '0';
                AutoPlayNote(note, noteDuration);
            }
            else if (c == ' ')
            {
                // 空格表示休止符，等待一段时间
                // 静默处理，不输出
            }
            else
            {
                // 忽略无效字符
                continue;
            }
            
            // 等待音符时长，但允许被取消
            try
            {
                await Task.Delay(noteDuration, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    static void AutoPlayNote(int note, int durationMilliseconds)
    {
        string fileName = $"XTLZ-{note}.mp3";
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        
        if (!File.Exists(filePath))
            return;

        try
        {
            var audioFile = new AudioFileReader(filePath);
            var player = new WaveOutEvent();
            player.Init(audioFile);
            
            // 添加到活跃播放器字典（使用负号区分自动播放，避免与手动播放冲突）
            int autoPlayKey = -note; // 使用负数作为键，避免与手动播放的正数键冲突
            activePlayers[autoPlayKey] = player;
            
            // 播放完成后自动释放资源并从字典移除
            player.PlaybackStopped += (sender, e) =>
            {
                audioFile.Dispose();
                player.Dispose();
                activePlayers.TryRemove(autoPlayKey, out _);
            };
            
            player.Play();
            
            // 设置定时器在指定时间后停止播放
            var timer = new System.Timers.Timer(durationMilliseconds);
            timer.Elapsed += (sender, e) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    player.Stop();
                }
                catch
                {
                    // 忽略停止时的异常
                }
            };
            timer.AutoReset = false;
            timer.Start();
        }
        catch
        {
            // 忽略播放错误
        }
    }

    static void RealisticPianoMode()
    {
        // 启动调试控制台
        StartDebugConsole();
        Console.WriteLine("\n=== 拟真钢琴键盘演奏模式 ===");
        ShowPianoKeyMapping();
        Console.WriteLine("\n演奏说明：");
        Console.WriteLine("• 按下映射表中的键播放对应音符");
        Console.WriteLine("• 空格键：延音踏板（按下保持，松开释放）");
        Console.WriteLine("• Esc：退出演奏模式");
        Console.WriteLine("• 按任意键开始演奏...");
        Console.ReadKey(intercept: true);
        
        bool exitMode = false;
        while (!exitMode)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                exitMode = true;
                StopAllPlayers();
                Console.WriteLine("\n退出拟真钢琴演奏模式。");
            }
            else if (key.Key == ConsoleKey.Spacebar)
            {
                sustainPedalPressed = !sustainPedalPressed;
                Console.WriteLine($"\n延音踏板 {(sustainPedalPressed ? "按下" : "释放")}");
            }
            else if (pianoKeyMapping.TryGetValue(key.Key, out int noteNumber))
            {
                PlayPitchShiftedNote(noteNumber);
            }
            else
            {
                // 忽略其他按键
            }
        }
        
        // 停止调试控制台
        StopDebugConsole();
    }

    static void ShowPianoKeyMapping()
    {
        string mappingFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "映射表.txt");
        if (File.Exists(mappingFilePath))
        {
            Console.WriteLine("\n========== 键盘映射表 ==========");
            string content = File.ReadAllText(mappingFilePath);
            Console.WriteLine(content);
        }
        
    }

    static string GetNoteName(int noteNumber)
    {
        string[] noteNames = {
            "C3", "C#3", "D3", "D#3", "E3", "F3", "F#3", "G3", "G#3", "A3", "A#3", "B3",
            "C4", "C#4", "D4", "D#4", "E4", "F4", "F#4", "G4", "G#4", "A4", "A#4", "B4",
            "C5", "C#5", "D5", "D#5", "E5", "F5", "F#5", "G5", "G#5", "A5", "A#5", "B5"
        };
        return noteNumber >= 0 && noteNumber < noteNames.Length ? noteNames[noteNumber] : $"N{noteNumber}";
    }

    static string GetProjectRootDirectory()
    {
        try
        {
            // 首先尝试当前目录
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            
            // 向上查找包含 .csproj 文件的目录
            for (int i = 0; i < 5; i++) // 最多向上查找5级目录
            {
                // 检查当前目录是否包含项目文件
                string projectFile = Path.Combine(currentDir, "XTLZ-Piano.csproj");
                if (File.Exists(projectFile))
                {
                    return currentDir;
                }
                
                // 向上一级目录
                DirectoryInfo? parent = Directory.GetParent(currentDir);
                if (parent == null)
                    break;
                    
                currentDir = parent.FullName;
            }
            
            // 如果没找到.csproj文件，检查当前目录是否包含XTLZ-O.mp3
            string baseAudioFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XTLZ-O.mp3");
            if (File.Exists(baseAudioFile))
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            
            // 返回当前目录作为后备
            return AppDomain.CurrentDomain.BaseDirectory;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取项目根目录时出错: {ex.Message}");
            // 返回当前目录作为后备
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
    
    static string GetPitchedNoteFilePath(int noteNumber)
    {
        // 音符文件存储在编译后程序目录的 Notes 文件夹中
        // 文件名格式：XTLZ-{音符名}.mp3，例如：XTLZ-C3.mp3, XTLZ-C#3.mp3
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string notesDir = Path.Combine(baseDir, "Notes");
        string noteName = GetNoteName(noteNumber);
        return Path.Combine(notesDir, $"XTLZ-{noteName}.mp3");
    }

    static void EnsurePitchShiftedNotesGenerated()
    {
        // 使用编译后程序目录的 Notes 文件夹
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string notesDir = Path.Combine(baseDir, "Notes");
        
        if (!Directory.Exists(notesDir))
        {
            Console.WriteLine($"警告：音符文件夹不存在: {notesDir}");
            Console.WriteLine("请确保在编译后程序目录下创建 Notes 文件夹，并放入所有音符文件。");
            Console.WriteLine($"预期位置: {notesDir}");
            return;
        }

        Console.WriteLine("检查音符文件...");
        int missingCount = 0;
        
        // 检查所有37个音符文件（0-35）
        for (int noteNumber = 0; noteNumber <= 35; noteNumber++)
        {
            string noteFilePath = GetPitchedNoteFilePath(noteNumber);
            string expectedFileName = $"XTLZ-{GetNoteName(noteNumber)}.mp3";
            if (!File.Exists(noteFilePath))
            {
                Console.WriteLine($"缺失: {GetNoteName(noteNumber)} ({expectedFileName})");
                missingCount++;
            }
        }
        
        if (missingCount > 0)
        {
            Console.WriteLine($"\n警告：缺失 {missingCount} 个音符文件。");
            Console.WriteLine("请确保在 Notes 文件夹中放入所有音符文件（XTLZ-C3.mp3 到 XTLZ-B5.mp3）。");
            Console.WriteLine($"检查目录: {notesDir}");
        }
        else
        {
            Console.WriteLine($"所有音符文件已就绪。目录: {notesDir}");
        }
    }

    static bool GeneratePitchedNoteFile(int noteNumber, string baseFilePath, string outputFilePath)
    {
        try
        {
            // 计算音高偏移（半音数）
            int semitoneOffset = noteNumber - BaseNoteNumber;
            
            // 计算频率比例：2^(半音偏移/12)
            double pitchRatio = Math.Pow(2.0, semitoneOffset / 12.0);
            
            // 加载基准音频文件
            using var audioFile = new AudioFileReader(baseFilePath);
            
            // 创建重采样器来改变音高
            int newSampleRate = (int)(audioFile.WaveFormat.SampleRate * pitchRatio);
            
            // 使用MediaFoundationResampler进行高质量重采样
            using var resampler = new MediaFoundationResampler(audioFile, newSampleRate);
            
            // 保存变调后的音频到文件
            WaveFileWriter.CreateWaveFile(outputFilePath, resampler);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"生成音符 {GetNoteName(noteNumber)} 时出错: {ex.Message}");
            return false;
        }
    }

    static void WriteDebugLog(string message)
    {
        try
        {
            lock (debugLogLock)
            {
                // 如果日志文件路径未设置，则创建
                if (debugLogFilePath == null)
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    debugLogFilePath = Path.Combine(baseDir, $"xtlz_piano_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                
                // 添加时间戳并写入日志
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logMessage = $"[{timestamp}] {message}";
                
                File.AppendAllText(debugLogFilePath, logMessage + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 忽略日志写入错误
        }
    }

    static void StartDebugConsole()
    {
        try
        {
            if (debugConsoleProcess != null && !debugConsoleProcess.HasExited)
                return;
                
            // 设置日志文件路径
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            debugLogFilePath = Path.Combine(baseDir, $"xtlz_piano_debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            // 创建初始日志消息
            File.WriteAllText(debugLogFilePath, $"=== 雪田梨子吟唱器调试日志 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n", Encoding.UTF8);
            
            // 启动新的 PowerShell 窗口来实时显示日志
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"Get-Content '{debugLogFilePath}' -Wait -Encoding UTF8\"",
                UseShellExecute = true,
                WorkingDirectory = baseDir
            };
            
            debugConsoleProcess = Process.Start(processStartInfo);
            WriteDebugLog("调试控制台已启动。\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动调试控制台时出错: {ex.Message}");
        }
    }

    static void StopDebugConsole()
    {
        try
        {
            if (debugConsoleProcess != null && !debugConsoleProcess.HasExited)
            {
                debugConsoleProcess.Kill();
                debugConsoleProcess.Dispose();
                debugConsoleProcess = null;
            }
            
            WriteDebugLog("调试控制台已停止。\n");
        }
        catch
        {
            // 忽略停止错误
        }
    }

    static void PlayPitchShiftedNote(int noteNumber)
    {
        // 检查是否已经有这个音符在播放（如果没有延音踏板）
        if (!sustainPedalPressed && activePlayers.ContainsKey(noteNumber))
        {
            // 如果已经在播放，先停止它（模拟钢琴键重新按下）
            if (activePlayers.TryRemove(noteNumber, out var existingPlayer))
            {
                existingPlayer.Stop();
                existingPlayer.Dispose();
            }
        }

        string noteFilePath = GetPitchedNoteFilePath(noteNumber);
        
        // 调试信息：显示加载的文件路径和音符信息
        string debugMessage = $"[调试] 音符: {GetNoteName(noteNumber)} (#{noteNumber}), 文件: {Path.GetFileName(noteFilePath)}";
        WriteDebugLog(debugMessage);
        
        if (!File.Exists(noteFilePath))
        {
            Console.WriteLine($"\n音符文件 {GetNoteName(noteNumber)} 不存在，请确保预生成文件已就绪。");
            Console.WriteLine($"搜索路径: {noteFilePath}");
            return;
        }

        try
        {
            var audioFile = new AudioFileReader(noteFilePath);
            var player = new WaveOutEvent();
            player.Init(audioFile);
            
            // 添加到活跃播放器字典
            activePlayers[noteNumber] = player;
            
            // 播放完成后自动释放资源
            player.PlaybackStopped += (sender, e) =>
            {
                audioFile.Dispose();
                player.Dispose();
                
                // 从字典中移除（如果还存在且没有延音踏板）
                if (!sustainPedalPressed)
                {
                    activePlayers.TryRemove(noteNumber, out _);
                }
            };
            
            player.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n播放音符 {GetNoteName(noteNumber)} 时出错: {ex.Message}");
        }
    }
}