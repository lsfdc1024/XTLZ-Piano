using Figgle;
using Figgle.Fonts;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Text;

namespace XTLZ_Piano;

internal class Program
{
    // 使用并发字典来管理多个播放器，允许多个音符同时播放
    private static readonly ConcurrentDictionary<int, WaveOutEvent> activePlayers = new();

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
        Console.WriteLine("2. 退出程序");
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
}