"""Detecta los cortes de un video con PySceneDetect y los escribe en JSON por la salida estándar.

Uso:
    python detect_scenes.py <video> [--threshold 27.0] [--min-scene-ms 500]

Salida (una línea):
    {"fps": 30.0, "duration_ms": 5500, "scenes": [{"start_ms": 0, "end_ms": 2000}, ...]}

Si el video no tiene cortes, "scenes" va vacío: quien llama decide tratarlo como una sola escena.
Cualquier error termina con código distinto de 0 y el motivo en la salida de error.
No modifica el archivo de entrada.
"""

import argparse
import json
import sys

from scenedetect import ContentDetector, SceneManager, open_video


def main() -> int:
    parser = argparse.ArgumentParser(description="Detecta cortes de escena y los informa en JSON.")
    parser.add_argument("video", help="Ruta del video a analizar.")
    parser.add_argument("--threshold", type=float, default=27.0, help="Sensibilidad del detector de contenido.")
    parser.add_argument("--min-scene-ms", type=int, default=500, help="Duración mínima de una escena.")
    args = parser.parse_args()

    try:
        video = open_video(args.video)
        fps = float(video.frame_rate)
        # El detector mide la duración mínima en cuadros.
        min_frames = max(1, round(args.min_scene_ms * fps / 1000))
        manager = SceneManager()
        manager.add_detector(ContentDetector(threshold=args.threshold, min_scene_len=min_frames))
        manager.detect_scenes(video, show_progress=False)
        scenes = manager.get_scene_list()
        duration_ms = round(video.duration.seconds * 1000) if video.duration is not None else None
    except Exception as error:  # noqa: BLE001 - cualquier fallo de lectura se informa igual a quien llama
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return 1

    result = {
        "fps": fps,
        "duration_ms": duration_ms,
        "scenes": [
            {"start_ms": round(start.seconds * 1000), "end_ms": round(end.seconds * 1000)}
            for start, end in scenes
        ],
    }
    print(json.dumps(result))
    return 0


if __name__ == "__main__":
    sys.exit(main())
