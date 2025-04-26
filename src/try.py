import time
from copy import deepcopy

import cv2 as cv

template = cv.imread("assets/lure4.png")
# template = cv.imread("shots/bob.png")
# template = cv.cvtColor(template, cv.COLOR_BGR2HSV)

bobber = cv.imread("shots/bob.png")
# bobber = cv.flip(bobber, 1)
bob = deepcopy(bobber)
# bobber = cv.cvtColor(bobber, cv.COLOR_BGR2HSV)

while True:
    try:
        INDEX = 0

        methods = [
            cv.TM_CCOEFF_NORMED,
            cv.TM_CCOEFF,
            cv.TM_CCORR_NORMED,
            cv.TM_CCORR,
            cv.TM_SQDIFF_NORMED,
            cv.TM_SQDIFF,
        ]
        match = cv.matchTemplate(bobber, template, methods[INDEX])
        minval, maxval, minloc, maxloc = cv.minMaxLoc(match)

        if methods[INDEX] == cv.TM_SQDIFF or methods[INDEX] == cv.TM_SQDIFF_NORMED:
            maxloc = minloc

        print(f"{INDEX}. maxval: {maxval:.2f}, minval: {minval:.2f}", end="\r")
        topleft = maxloc
        bottomright = (topleft[0] + 20, topleft[1] + 20)
        cv.rectangle(bob, topleft, bottomright, (0, 0, 255), 2)
        cv.imshow("match", bob)

        key = cv.waitKey(1)
        if key == ord("q"):
            print()
            break

        time.sleep(0.2)

    except KeyboardInterrupt:
        print("Exiting.")
        exit(0)
