namespace MeowsCut.Ffmpeg.Tests.Probing;

/// <summary>
/// Ответы ffprobe, снятые с реальных файлов и обрезанные до значимых полей.
/// Маппер должен переживать всё, что встречается в природе.
/// </summary>
internal static class FfprobeFixtures
{
    /// <summary>Обычный mp4: H.264 + AAC, всё на месте.</summary>
    public const string StandardMp4 =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "h264",
              "codec_long_name": "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
              "profile": "High",
              "codec_type": "video",
              "width": 1280,
              "height": 720,
              "sample_aspect_ratio": "1:1",
              "pix_fmt": "yuv420p",
              "r_frame_rate": "30/1",
              "avg_frame_rate": "30/1",
              "duration": "6.000000",
              "bit_rate": "2757984",
              "disposition": { "attached_pic": 0 },
              "tags": { "language": "und" }
            },
            {
              "index": 1,
              "codec_name": "aac",
              "codec_type": "audio",
              "sample_rate": "44100",
              "channels": 1,
              "channel_layout": "mono",
              "duration": "6.005333",
              "bit_rate": "69256",
              "tags": { "language": "und" }
            }
          ],
          "format": {
            "filename": "clip.mp4",
            "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
            "format_long_name": "QuickTime / MOV",
            "duration": "6.005333",
            "size": "2170134",
            "bit_rate": "2891008"
          }
        }
        """;

    /// <summary>Вертикальное видео с телефона: поворот в side_data, кадр хранится горизонтально.</summary>
    public const string RotatedPhoneVideo =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "h264",
              "codec_type": "video",
              "width": 1920,
              "height": 1080,
              "pix_fmt": "yuv420p",
              "r_frame_rate": "30/1",
              "avg_frame_rate": "30/1",
              "duration": "12.000000",
              "side_data_list": [
                { "side_data_type": "Display Matrix", "rotation": -90 }
              ]
            }
          ],
          "format": {
            "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
            "duration": "12.000000",
            "size": "10485760"
          }
        }
        """;

    /// <summary>Старый вариант поворота — тегом rotate.</summary>
    public const string RotatedByTag =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "h264",
              "codec_type": "video",
              "width": 1920,
              "height": 1080,
              "pix_fmt": "yuv420p",
              "r_frame_rate": "30/1",
              "avg_frame_rate": "30/1",
              "tags": { "rotate": "270" }
            }
          ],
          "format": { "format_name": "mov,mp4", "duration": "5.0", "size": "1024" }
        }
        """;

    /// <summary>mp3 с обложкой: видеопоток есть, но это картинка — открывать такое нельзя.</summary>
    public const string Mp3WithCoverArt =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "mjpeg",
              "codec_type": "video",
              "width": 600,
              "height": 600,
              "pix_fmt": "yuvj420p",
              "r_frame_rate": "90000/1",
              "avg_frame_rate": "0/0",
              "disposition": { "attached_pic": 1 }
            },
            {
              "index": 1,
              "codec_name": "mp3",
              "codec_type": "audio",
              "sample_rate": "44100",
              "channels": 2,
              "channel_layout": "stereo",
              "bit_rate": "320000"
            }
          ],
          "format": { "format_name": "mp3", "duration": "180.0", "size": "7200000" }
        }
        """;

    /// <summary>
    /// Запись с экрана: переменная частота кадров, битрейта потока нет, длительности
    /// в format тоже нет — всё, что ломает наивный маппер.
    /// </summary>
    public const string VariableFrameRateWebm =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "vp9",
              "codec_type": "video",
              "width": 2560,
              "height": 1440,
              "pix_fmt": "yuv420p",
              "r_frame_rate": "1000/1",
              "avg_frame_rate": "24000/1001",
              "duration": "42.500000"
            }
          ],
          "format": { "format_name": "matroska,webm", "size": "88000000" }
        }
        """;

    /// <summary>Видео с альфа-каналом — стикеры Telegram именно такие.</summary>
    public const string AlphaWebm =
        """
        {
          "streams": [
            {
              "index": 0,
              "codec_name": "vp9",
              "codec_type": "video",
              "width": 512,
              "height": 512,
              "pix_fmt": "yuva420p",
              "r_frame_rate": "30/1",
              "avg_frame_rate": "30/1",
              "duration": "2.900000"
            }
          ],
          "format": { "format_name": "matroska,webm", "duration": "2.900000", "size": "245000" }
        }
        """;
}
