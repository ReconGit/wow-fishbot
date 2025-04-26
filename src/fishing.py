import random
import time

import cv2 as cv
import keyboard
import pyautogui as py

from audio import AudioAgent


class FishAgent:
    """Agent that cooperates with screen agent, audio agent and uses the information to control the game"""

    def __init__(self, screen_agent, audio_agent: AudioAgent):
        self.agent = screen_agent
        self.audio_agent = audio_agent
        self.lure_template = cv.imread("assets/lure1.png")
        self.template2 = cv.imread("assets/lure2.png")

    def cast_lure(self):
        # print("Casting lure..")
        py.hotkey("ctrl", "6", interval=random.uniform(0.005, 0.01))
        time.sleep(random.uniform(2, 3))
        self.find_lure()

    def find_lure(self):
        try:
            # print("Finding lure..")
            lure_location = cv.matchTemplate(self.agent.image, self.lure_template, cv.TM_CCOEFF_NORMED)
            minval, maxval, minloc, maxloc = cv.minMaxLoc(lure_location)

            print(f"match val: {maxval}")
            if maxval < 0.715:
                lure_location = cv.matchTemplate(self.agent.image, self.template2, cv.TM_CCOEFF_NORMED)
                minval, maxval, minloc, maxloc = cv.minMaxLoc(lure_location)
                print(f"new match val: {maxval}")

            self.lure_location = maxloc
            self.move_to_lure()

        except Exception as e:
            print(f"Error: {e}")
            self.pull_lure()

    def move_to_lure(self):
        try:
            # print("Moving to lure..")
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

                if self.audio_agent.audio_spike:
                    print("Bite detected!")
                    break
                time.sleep(0.01)
            self.pull_lure()

        except Exception as e:
            print(f"Error: {e}")
            self.pull_lure()

    def pull_lure(self):
        # print("Pulling lure..")
        time.sleep(random.uniform(0.005, 0.01))
        py.rightClick(interval=random.uniform(0.005, 0.01))
        time.sleep(random.uniform(0.1, 0.2))
        self.cast_lure()
