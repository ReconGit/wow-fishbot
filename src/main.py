import time
from threading import Thread

import cv2 as cv
import numpy as np
from PIL import ImageGrab

from audio import AudioAgent
from fishing import FishAgent


def main():
    try:
        screen_agent = ScreenAgent()
        audio_agent = AudioAgent()
        fish_agent = FishAgent(screen_agent, audio_agent)
        while True:
            print_menu()
            command = input("Command: ").upper()
            handle_command(screen_agent, audio_agent, fish_agent, command)
    except KeyboardInterrupt:
        exit(0)


class ScreenAgent:
    """Captures the screen and streams the screenshots to a variable"""

    def __init__(self):
        self.image: np.ndarray
        print("Agent initialized")

    def capture_screen(self):
        while True:
            # t0 = time.time()
            image = ImageGrab.grab()
            image = np.array(image)
            self.image = cv.cvtColor(image, cv.COLOR_RGB2BGR)
            # cv.imshow("screen capture", agent.image)

            key = cv.waitKey(1)
            if key == ord("q"):
                break

            time.sleep(0.001)
            # elapsed_time = time.time() - t0
            # print(f"FPS: {1 / elapsed_time:.2f}", end="\r")


def print_menu():
    print("Enter a command:")
    print(" S - Screen fishing")
    print(" Q - Quit")


def handle_command(
    screen_agent: ScreenAgent,
    audio_agent: AudioAgent,
    fish_agent: FishAgent,
    command: str,
):
    if command == "S":
        capture_screen_t = Thread(
            target=screen_agent.capture_screen,
            args=(screen_agent,),
            daemon=True,
        )
        capture_screen_t.start()

        audio_capture_t = Thread(
            target=audio_agent.capture_audio,
            args=(),
            daemon=True,
        )
        audio_capture_t.start()

        print("Starting fishing in 3 seconds.")
        time.sleep(3)
        fishing_t = Thread(
            target=fish_agent.cast_lure,
            args=(),
            daemon=True,
        )
        fishing_t.start()

    elif command == "Q":
        print("Quitting.")
        exit(0)
    else:
        print("Invalid command.")


if __name__ == "__main__":
    main()
