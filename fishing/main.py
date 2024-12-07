import time
from threading import Thread

import cv2 as cv
import numpy as np
from PIL import ImageGrab

from audio import AudioAgent
from fishing import FishAgent


def main():
    agent = Agent()
    audio_agent = AudioAgent()
    while True:
        print_menu()
        command = input("Command: ").upper()
        handle_command(agent, audio_agent, command)


class Agent:
    def __init__(self):
        self.image_bgr: np.ndarray
        self.image_hsv: np.ndarray
        print("Agent initialized")


def capture_screen(agent: Agent):
    while True:
        # t0 = time.time()
        image = ImageGrab.grab()
        image = np.array(image)
        agent.image_bgr = cv.cvtColor(image, cv.COLOR_RGB2BGR)
        agent.image_hsv = cv.cvtColor(image, cv.COLOR_RGB2HSV)  # was working with BGR
        # agent.image_hsv = agent.image_bgr
        # cv.imshow("screen capture", agent.image_hsv)

        key = cv.waitKey(1)
        if key == ord("q"):
            break

        time.sleep(0.0001)
        # elapsed_time = time.time() - t0
        # print(f"FPS: {1 / elapsed_time:.2f}", end="\r")


def print_menu():
    print("Enter a command:")
    print(" S - Screen fishing")
    print(" Q - Quit")


def handle_command(agent: Agent, audio_agent: AudioAgent, command: str):
    if command == "S":
        # start screen capture thread
        capture_screen_t = Thread(
            target=capture_screen,
            args=(agent,),
            daemon=True,
        )
        capture_screen_t.start()

        # start audio capture thread
        audio_capture_t = Thread(
            target=audio_agent.capture_audio,
            args=(),
            daemon=True,
        )
        audio_capture_t.start()

        print("Starting in 3 seconds.")
        time.sleep(3)
        fish_agent = FishAgent(agent, audio_agent, asset="assets/lure4.png")
        fish_agent.run()

    elif command == "Q":
        print("Quitting.")
        exit(0)
    else:
        print("Invalid command.")


if __name__ == "__main__":
    main()
