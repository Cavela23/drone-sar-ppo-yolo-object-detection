from ultralytics import YOLO
import cv2
import socket
import numpy as np
import struct
import threading
import time

model = YOLO(r"C:\Kuliah\_output_\runs\detect\train\weights\best.pt")

frame_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
frame_sock.connect(("127.0.0.1", 5006))

detect_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
detect_sock.connect(("127.0.0.1", 5005))

CONFIDENCE_THRESHOLD = 0.55

latest_frame = None
latest_annotated = None
frame_lock = threading.Lock()
running = True

def recv_exact(sock, n):
    data = b""
    while len(data) < n:
        packet = sock.recv(n - len(data))
        if not packet:
            return None
        data += packet
    return data

def frame_receiver():
    global latest_frame, running
    while running:
        try:
            size_data = recv_exact(frame_sock, 4)
            if size_data is None:
                break
            frame_size = struct.unpack('<I', size_data)[0]
            frame_data = recv_exact(frame_sock, frame_size)
            if frame_data is None:
                break
            nparr = np.frombuffer(frame_data, np.uint8)
            frame = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
            if frame is not None:
                with frame_lock:
                    latest_frame = frame
        except Exception as e:
            print(f"[Receiver Error] {e}")
            break

def yolo_worker():
    global latest_annotated, running
    while running:
        frame_to_process = None
        with frame_lock:
            if latest_frame is not None:
                frame_to_process = latest_frame.copy()

        if frame_to_process is None:
            time.sleep(0.01)
            continue

        try:
            results = model(frame_to_process, verbose=False, conf=CONFIDENCE_THRESHOLD)

            # === PERBAIKAN: pilih detection dengan confidence tertinggi ===

            best_conf = 0
            best_cx = None
            best_w = 0
            best_h = 0

            for r in results:
                for box in r.boxes:
                    if int(box.cls[0]) == 0:  # class 0 = person
                        conf = float(box.conf[0])
                        x1, y1, x2, y2 = box.xyxy[0]
                        w, h = x2 - x1, y2 - y1
                        aspect = w / h if h > 0 else 0

                        if 0.25 < aspect < 4.0 and conf > best_conf:
                            best_cx = float((x1 + x2) / 2) / frame_to_process.shape[1]
                            best_conf = conf
                            best_w = float(w)
                            best_h = float(h)

            # Kirim ke Unity
            if best_cx is not None:
                # Hitung area bbox ternormalisasi (0.0 - 1.0)
                frame_h, frame_w = frame_to_process.shape[:2]
                bbox_area = float((best_w * best_h) / (frame_w * frame_h))
                msg = f"PERSON {best_cx:.4f} {bbox_area:.4f}\n".encode()
                print(f"✅ PERSON conf={best_conf:.2f} cx={best_cx:.3f} area={bbox_area:.3f}")
            else:
                msg = b"NONE\n"

            detect_sock.sendall(msg)  # sendall lebih aman dari send

            # Annotated frame untuk display
            annotated = results[0].plot()
            label = "✅ PERSON FOUND!" if best_cx else "🔍 Scanning..."
            color = (0, 255, 0) if best_cx else (0, 0, 200)
            cv2.putText(annotated, label, (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, color, 2)

            with frame_lock:
                latest_annotated = annotated

        except Exception as e:
            print(f"[YOLO Error] {e}")

        time.sleep(0.066)  # ~15 FPS

t1 = threading.Thread(target=frame_receiver, daemon=True)
t2 = threading.Thread(target=yolo_worker, daemon=True)
t1.start()
t2.start()

print("✅ Semua thread berjalan. Tekan ESC untuk keluar.")

cv2.namedWindow("Drone YOLO", cv2.WINDOW_NORMAL)
cv2.resizeWindow("Drone YOLO", 320, 240)
cv2.moveWindow("Drone YOLO", 0, 0)

while running:
    display = None
    with frame_lock:
        if latest_annotated is not None:
            display = latest_annotated.copy()

    if display is not None:
        cv2.imshow("Drone YOLO", display)

    key = cv2.waitKey(1) & 0xFF
    if key == 27:
        running = False
        break

    time.sleep(0.033)

frame_sock.close()
detect_sock.close()
cv2.destroyAllWindows()
print("👋 YOLO detector closed.")