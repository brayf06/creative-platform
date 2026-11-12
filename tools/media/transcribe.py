"""Transcribe el audio de un video con Whisper local (faster-whisper) y escribe el resultado en JSON.

Uso:
    python transcribe.py <video> [--model small] [--language es] [--model-dir <carpeta>] [--ffmpeg ffmpeg]

Salida (una línea):
    {"language": "es", "model": "faster-whisper:small",
     "lines": [{"start_ms": 0, "end_ms": 2400, "text": "..."}, ...]}

El audio se decodifica con FFmpeg (mono, 16 kHz) y se le pasa a Whisper ya en memoria: así no se depende del
decodificador propio de faster-whisper, que no es compatible con la versión de PyAV que se instala hoy.
La primera vez que se usa un modelo se descarga a --model-dir (o a la cache de Hugging Face del usuario).
Un video sin voz devuelve "lines" vacío. Cualquier error termina con código distinto de 0 y el motivo en la
salida de error. No modifica el archivo de entrada. Corre en CPU.
"""

import argparse
import json
import subprocess
import sys

SAMPLE_RATE = 16000


def read_audio(ffmpeg: str, video: str):
    """Devuelve el audio del video como float32 mono a 16 kHz."""
    import numpy

    process = subprocess.run(
        [ffmpeg, "-v", "error", "-i", video, "-vn", "-f", "f32le", "-ac", "1", "-ar", str(SAMPLE_RATE), "-"],
        capture_output=True,
        check=False,
    )
    if process.returncode != 0:
        detail = process.stderr.decode("utf-8", errors="replace").strip().splitlines()
        raise RuntimeError(detail[-1] if detail else "FFmpeg no pudo leer el audio.")
    return numpy.frombuffer(process.stdout, dtype=numpy.float32)


def split_into_phrases(segments, max_words: int = 8):
    """Parte la transcripción en frases cortas usando los tiempos por palabra.

    Whisper devuelve tramos largos (a veces todo el audio en uno). Para poder asignar el texto a cada escena
    se corta después de un signo de puntuación o al llegar a max_words palabras.
    """
    lines = []
    words = []

    def flush():
        if words:
            text = "".join(word.word for word in words).strip()
            if text:
                lines.append({"start_ms": round(words[0].start * 1000), "end_ms": round(words[-1].end * 1000), "text": text})
            words.clear()

    for segment in segments:
        for word in segment.words or []:
            words.append(word)
            if word.word.rstrip().endswith((".", ",", "?", "!", ";", ":")) or len(words) >= max_words:
                flush()
        flush()
    return lines


def main() -> int:
    parser = argparse.ArgumentParser(description="Transcribe un video y lo informa en JSON.")
    parser.add_argument("video", help="Ruta del video a transcribir.")
    parser.add_argument("--model", default="small", help="Tamaño del modelo: tiny, base, small, medium, large-v3.")
    parser.add_argument("--language", default=None, help="Idioma (es, en...). Sin indicar, se detecta.")
    parser.add_argument("--model-dir", default=None, help="Carpeta donde guardar los modelos descargados.")
    parser.add_argument("--ffmpeg", default="ffmpeg", help="Ejecutable de FFmpeg.")
    args = parser.parse_args()

    try:
        # Se importa acá para que un error de instalación se informe igual que cualquier otro.
        from faster_whisper import WhisperModel

        audio = read_audio(args.ffmpeg, args.video)
        language = args.language
        lines = []
        if audio.size > 0:
            model = WhisperModel(args.model, device="cpu", compute_type="int8", download_root=args.model_dir)
            # vad_filter descarta los tramos sin voz: evita texto inventado sobre música o silencio.
            segments, info = model.transcribe(audio, language=args.language, vad_filter=True, word_timestamps=True)
            lines = split_into_phrases(segments)
            language = info.language
    except Exception as error:  # noqa: BLE001 - cualquier fallo se informa igual a quien llama
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return 1

    # ensure_ascii deja la salida en ASCII puro: no depende de la codificación de la consola de Windows.
    print(json.dumps({"language": language, "model": f"faster-whisper:{args.model}", "lines": lines}, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
