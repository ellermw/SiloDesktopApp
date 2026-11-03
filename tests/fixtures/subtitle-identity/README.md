# Synthetic subtitle identity media

`tracks.mkv` contains 20 seconds of silent mono PCM audio and two authored
SubRip streams from the adjacent text files. It contains no user media.
Matroska TrackNumbers are 1 (audio), 2 (English), and 3 (Japanese); absolute
FFmpeg indices are 0, 1, and 2. The native fixture inserts absent index-only
rows into the synthetic server inventory without changing the media bytes.

Generation command (FFmpeg N-127893-g9a861e968):

```powershell
ffmpeg -f lavfi -i anullsrc=r=8000:cl=mono -i english.srt -i japanese.srt -map 0:a -map 1:s -map 2:s -c:a pcm_u8 -c:s srt -metadata:s:s:0 language=eng -metadata:s:s:1 language=jpn -t 20 tracks.mkv
```

Frozen media SHA256: `A0EEBB0C91BC5F7F17DC08F91E290034DD5CA31F4018F7F9462856C1EB6B8AF3`.

The resolver follows the [mpv track-list contract](https://mpv.io/manual/master/#property-list):
`id` is the selectable SID, `src-id` is the container ID, and `ff-index` is
only guaranteed to use FFmpeg's namespace with libavformat. The builtin
Matroska demuxer's approximate FFmpeg index alone is insufficient identity.
