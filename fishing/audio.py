import time

import numpy as np
import pyaudio

FORMAT = pyaudio.paInt16
CHANNELS = 2
SAMPLE_RATE = 44100
CHUNK = 1024


class AudioAgent:
    """Agent to detect bite based on audio."""

    def __init__(self):
        self.audio_spike = False
        self.p = pyaudio.PyAudio()
        self.stream = self.p.open(
            format=FORMAT,
            channels=CHANNELS,
            rate=SAMPLE_RATE,
            frames_per_buffer=CHUNK,
            input=True,
            input_device_index=2,
        )

    def capture_audio(self):
        try:
            while True:
                raw_data = self.stream.read(CHUNK)
                data = np.frombuffer(raw_data, dtype=np.int16)
                rms = self.calculate_rms(data)
                # print(f"RMS: {rms:.3f}        ", end="\r")  # RMS value normalized to [0, 1]
                if rms > 0.05:
                    self.audio_spike = True
                    print("Audio spike detected!")
                    time.sleep(0.3)
                    self.audio_spike = False
        except KeyboardInterrupt:
            print("\nStopped.")
            self.stream.stop_stream()
            self.stream.close()
            self.p.terminate()

    def calculate_rms(self, data):
        """Calculate the Root Mean Square (RMS) of the audio signal."""
        normalized_data = data / 32767.0  # Normalize to [-1, 1] range
        return np.sqrt(np.mean(normalized_data**2))


if __name__ == "__main__":
    agent = AudioAgent()
    agent.capture_audio()
