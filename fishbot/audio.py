import time

import numpy as np
import pyaudio

FORMAT = pyaudio.paInt16
CHANNELS = 2
SAMPLE_RATE = 44100
CHUNK = 1024

DEVICE_INDEX = 2
THRESHOLD = 0.04


class AudioAgent:
    """Agent to detect bite based on audio."""

    def __init__(self):
        self.audio_spike = False
        self.pa = pyaudio.PyAudio()
        self.stream = self.pa.open(
            format=FORMAT,
            channels=CHANNELS,
            rate=SAMPLE_RATE,
            frames_per_buffer=CHUNK,
            input=True,
            input_device_index=DEVICE_INDEX,
        )

    def capture_audio(self):
        try:
            while True:
                raw_data = self.stream.read(CHUNK)
                data = np.frombuffer(raw_data, dtype=np.int16)
                rms = self.calculate_rms(data)
                # print(f"RMS: {rms:.3f}        ", end="\r")  # RMS value normalized to [0, 1]
                if rms > THRESHOLD:
                    self.audio_spike = True
                    # print("Audio spike detected!")
                    time.sleep(0.5)
                    self.audio_spike = False
                time.sleep(0.01)

        except KeyboardInterrupt:
            print("\nStopped.")
            self.stream.stop_stream()
            self.stream.close()
            self.pa.terminate()

    def calculate_rms(self, data):
        """Calculate the Root Mean Square (RMS) of the audio signal."""
        normalized_data = data / 32767.0  # Normalize to [-1, 1] range
        return np.sqrt(np.mean(normalized_data**2))

    def list_audio_devices(self):
        device_count = self.pa.get_device_count()
        for i in range(device_count):
            device_info = self.pa.get_device_info_by_index(i)
            print(f"Index {i}: {device_info['name']}")
            print(f"  Input Channels: {device_info['maxInputChannels']}")
            print(f"  Output Channels: {device_info['maxOutputChannels']}")
            print(f"  Default Sample Rate: {device_info['defaultSampleRate']}")
        self.pa.terminate()


if __name__ == "__main__":
    agent = AudioAgent()
    # agent.capture_audio()
    agent.list_audio_devices()
