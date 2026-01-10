using Figgle;
using Figgle.Fonts;
using NAudio.Wave;
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

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        // 显示艺术字体标题
        Console.WriteLine(FiggleFonts.Slant.Render("lsfStudio"));
        Console.WriteLine("项目名称：雪田梨子吟唱器");
        Console.WriteLine();

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
                    PlayFromNotationFile();
                    break;
                case "3":
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
        Console.WriteLine("1. 开始演奏");
        Console.WriteLine("2. 读取简谱文件并自动演奏");
        Console.WriteLine("3. 退出程序");
        Console.Write("请选择（输入数字）: ");
    }

    static void PlayMode()
    {
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
            
            Console.Write($"{note}");
            
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
        const int noteDuration = 500; // 每个音符播放时长（毫秒）
        
        foreach (char c in notation)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            if (c >= '1' && c <= '7')
            {
                int note = c - '0';
                AutoPlayNote(note, noteDuration);
                Console.Write($"{note}");
            }
            else if (c == ' ')
            {
                // 空格表示休止符，等待一段时间
                Console.Write(" ");
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
}