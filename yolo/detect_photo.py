from ultralytics import YOLO
from PIL import Image
import cv2
import os

# Load the trained model
model = YOLO(r"C:\Kuliah\_output_\runs\detect\train\weights\best.pt")

def detect_objects_webcam():
    """
    Detect objects from webcam using the trained YOLO model in real-time
    """
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("Cannot open webcam")
        return

    while True:
        ret, frame = cap.read()
        if not ret:
            print("Failed to grab frame")
            break

        # Run inference
        results = model.predict(source=frame, conf=0.25, verbose=False)
        for result in results:
            annotated_frame = result.plot()
            cv2.imshow("Detection Results", annotated_frame)

        # Exit on 'q' key
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    # Deteksi objek dari webcam
    detect_objects_webcam()