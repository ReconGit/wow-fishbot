import random
import time
from threading import Thread

import cv2 as cv
import keyboard
import pyautogui as py

from audio import AudioAgent


class FishAgent:
    def __init__(self, agent, audio_agent: AudioAgent, asset: str):
        self.agent = agent
        self.audio_agent = audio_agent
        self.lure_template = cv.imread(asset)
        self.thread = None

    def cast_lure(self):
        print("Casting lure..")
        py.hotkey("ctrl", "6", interval=random.uniform(0.005, 0.01))
        time.sleep(random.uniform(2, 3))
        self.find_lure()

    def find_lure(self):
        try:
            print("Finding lure..")
            lure_location = cv.matchTemplate(self.agent.image_bgr, self.lure_template, cv.TM_CCOEFF_NORMED)
            min_val, max_val, min_loc, max_loc = cv.minMaxLoc(lure_location)
            self.lure_location = max_loc
            self.move_to_lure()
        except Exception as e:
            print(f"Error: {e}")
            self.pull_lure()

    def move_to_lure(self):
        try:
            print("Moving to lure..")
            if self.lure_location:
                py.moveTo(
                    x=self.lure_location[0] + 24,
                    y=self.lure_location[1],
                    duration=random.uniform(0.5, 1),
                    tween=py.easeInOutQuad,
                )
                self.watch_lure()
            else:
                print("Warning: Attempted to move to lure_location, but lure_location is None.")
                self.pull_lure()
        except Exception as e:
            print(f"Error: {e}")
            self.pull_lure()

    def watch_lure(self):
        try:
            print("Watching lure..")
            t0 = time.time()
            while True:
                if keyboard.is_pressed("e"):
                    print("User pressed 'E'. Exiting loop.")
                    break

                time_elapsed = time.time() - t0
                if time_elapsed > 28:
                    print("No bite detected..")
                    break

                # pixel = self.agent.image_hsv[self.lure_location[1] + 24][self.lure_location[0]]
                # if pixel[0] > 90:
                #     print("Bite detected!")
                #     break

                if self.audio_agent.audio_spike:
                    print("Bite detected!")
                    break

                time.sleep(0.01)
            self.pull_lure()
        except Exception as e:
            print(f"Error: {e}")
            self.pull_lure()

    def pull_lure(self):
        print("Pulling lure..")
        time.sleep(random.uniform(0.005, 0.01))
        py.rightClick()
        time.sleep(random.uniform(0.1, 0.2))
        self.run()

    def run(self):
        if self.agent.image_bgr is None:
            print("Image capture not found! Start screen capture first.")
            return

        print("Starting fishing thread in 0.5 seconds..")
        time.sleep(random.uniform(0.1, 0.2))

        self.fishing_thread = Thread(
            target=self.cast_lure,
            args=(),
            name="fishing thread",
            daemon=True,
        )
        self.fishing_thread.start()
