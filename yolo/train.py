# -*- coding: utf-8 -*-
"""
DETEKSI OBJEK MANUSIA DENGAN CNN CUSTOM + BOUNDING BOX
Lingkungan : Google Colab (GPU)
Dataset    : CSV annotations (filename, width, height, class, xmin, ymin, xmax, ymax)
Struktur   :
  Dataset/
    train/ -> gambar + _annotations.csv
    valid/ -> gambar + _annotations.csv
    test/  -> gambar + _annotations.csv
"""

# ─────────────────────────────────────────────
# 1. INSTALL & IMPORT
# ─────────────────────────────────────────────
# Jalankan cell ini lebih dulu di Colab:
# !pip install tensorflow opencv-python-headless matplotlib pandas scikit-learn

import os
import cv2
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib.patches as patches
import tensorflow as tf

from tensorflow.keras import layers, models, optimizers, callbacks
from sklearn.metrics import mean_absolute_error

print("TensorFlow versi :", tf.__version__)
print("GPU tersedia      :", tf.config.list_physical_devices('GPU'))


# ─────────────────────────────────────────────
# 2. KONFIGURASI — SESUAIKAN JIKA PERLU
# ─────────────────────────────────────────────
# Mount Google Drive terlebih dahulu jika dataset ada di Drive:
# from google.colab import drive
# drive.mount('/content/drive')
# BASE_DIR = '/content/drive/MyDrive/Dataset'

BASE_DIR   = '/content/Dataset'          # <-- ubah sesuai lokasi kamu
TRAIN_DIR  = os.path.join(BASE_DIR, 'train')
VALID_DIR  = os.path.join(BASE_DIR, 'valid')
TEST_DIR   = os.path.join(BASE_DIR, 'test')

IMG_SIZE   = 416          # Input CNN: 416x416 (standar deteksi objek)
BATCH_SIZE = 32
EPOCHS     = 50
LR         = 1e-4


# ─────────────────────────────────────────────
# 3. LOAD & NORMALISASI ANNOTATIONS
# ─────────────────────────────────────────────
def load_annotations(folder):
    """
    Membaca _annotations.csv dan menormalisasi bounding box ke [0,1].
    Jika 1 gambar punya banyak box, diambil box PERTAMA saja
    (CNN single-output hanya prediksi 1 box per gambar).
    Untuk multi-box gunakan YOLO/Faster R-CNN.
    """
    csv_path = os.path.join(folder, '_annotations.csv')
    df = pd.read_csv(csv_path)

    # Hapus baris kosong / NaN
    df = df.dropna(subset=['filename', 'xmin', 'ymin', 'xmax', 'ymax'])

    # Normalisasi koordinat bounding box ke [0,1]
    df['xmin_n'] = df['xmin'] / df['width']
    df['ymin_n'] = df['ymin'] / df['height']
    df['xmax_n'] = df['xmax'] / df['width']
    df['ymax_n'] = df['ymax'] / df['height']

    # Clip agar tidak melebihi [0,1] akibat anotasi yang sedikit error
    for col in ['xmin_n', 'ymin_n', 'xmax_n', 'ymax_n']:
        df[col] = df[col].clip(0.0, 1.0)

    # Ambil 1 baris per gambar (box pertama / box terbesar)
    df['area'] = (df['xmax_n'] - df['xmin_n']) * (df['ymax_n'] - df['ymin_n'])
    df = df.sort_values('area', ascending=False)
    df = df.drop_duplicates(subset='filename', keep='first')

    return df.reset_index(drop=True)


df_train = load_annotations(TRAIN_DIR)
df_valid = load_annotations(VALID_DIR)
df_test  = load_annotations(TEST_DIR)

print(f"Train : {len(df_train)} gambar")
print(f"Valid : {len(df_valid)} gambar")
print(f"Test  : {len(df_test)}  gambar")
print("\nContoh data train:")
print(df_train[['filename', 'xmin_n', 'ymin_n', 'xmax_n', 'ymax_n']].head(3))


# ─────────────────────────────────────────────
# 4. DATA GENERATOR (tf.data pipeline)
# ─────────────────────────────────────────────
def augment_image(image):
    """Augmentasi ringan: flip, brightness, contrast."""
    image = tf.image.random_flip_left_right(image)
    image = tf.image.random_brightness(image, max_delta=0.15)
    image = tf.image.random_contrast(image, lower=0.85, upper=1.15)
    image = tf.clip_by_value(image, 0.0, 1.0)
    return image


def load_sample(img_path, bbox):
    """Load 1 gambar + resize + normalisasi pixel."""
    img = tf.io.read_file(img_path)
    img = tf.image.decode_jpeg(img, channels=3)
    img = tf.image.resize(img, [IMG_SIZE, IMG_SIZE])
    img = img / 255.0
    return img, bbox


def build_dataset(df, img_dir, augment=False, batch_size=BATCH_SIZE):
    paths  = [os.path.join(img_dir, f) for f in df['filename']]
    bboxes = df[['xmin_n', 'ymin_n', 'xmax_n', 'ymax_n']].values.astype('float32')

    dataset = tf.data.Dataset.from_tensor_slices(
        (paths, bboxes)
    )
    dataset = dataset.map(load_sample, num_parallel_calls=tf.data.AUTOTUNE)

    if augment:
        dataset = dataset.map(
            lambda img, bb: (augment_image(img), bb),
            num_parallel_calls=tf.data.AUTOTUNE
        )

    dataset = (dataset
               .shuffle(1000 if augment else 1)
               .batch(batch_size)
               .prefetch(tf.data.AUTOTUNE))
    return dataset


ds_train = build_dataset(df_train, TRAIN_DIR, augment=True)
ds_valid = build_dataset(df_valid, VALID_DIR, augment=False)
ds_test  = build_dataset(df_test,  TEST_DIR,  augment=False)

print("\nShape batch pertama:")
for img_batch, bbox_batch in ds_train.take(1):
    print("  Gambar :", img_batch.shape)    # (32, 416, 416, 3)
    print("  BBox   :", bbox_batch.shape)   # (32, 4)


# ─────────────────────────────────────────────
# 5. ARSITEKTUR CNN CUSTOM
# ─────────────────────────────────────────────
def conv_block(x, filters, kernel=3, pool=True):
    """Conv2D + BatchNorm + ReLU + optional MaxPool."""
    x = layers.Conv2D(filters, kernel, padding='same', use_bias=False)(x)
    x = layers.BatchNormalization()(x)
    x = layers.Activation('relu')(x)
    if pool:
        x = layers.MaxPooling2D(2)(x)
    return x


def build_cnn_detector(input_shape=(IMG_SIZE, IMG_SIZE, 3)):
    inputs = layers.Input(shape=input_shape)

    # ── Encoder (feature extraction) ──────────────
    x = conv_block(inputs,  32)          # 208x208
    x = conv_block(x,       64)          # 104x104
    x = conv_block(x,      128)          # 52x52
    x = conv_block(x,      256)          # 26x26
    x = conv_block(x,      512)          # 13x13

    # Extra conv tanpa pooling untuk memperkaya fitur
    x = conv_block(x,      512, pool=False)
    x = conv_block(x,      256, pool=False)

    # ── Head (regression) ─────────────────────────
    x = layers.GlobalAveragePooling2D()(x)
    x = layers.Dense(1024, activation='relu')(x)
    x = layers.Dropout(0.4)(x)
    x = layers.Dense(512, activation='relu')(x)
    x = layers.Dropout(0.3)(x)

    # Output: 4 nilai (xmin, ymin, xmax, ymax) dalam [0,1]
    outputs = layers.Dense(4, activation='sigmoid', name='bbox_output')(x)

    model = models.Model(inputs, outputs, name='PersonDetectorCNN')
    return model


model = build_cnn_detector()
model.summary()


# ─────────────────────────────────────────────
# 6. LOSS FUNCTION: IoU LOSS + MSE
# ─────────────────────────────────────────────
def iou_loss(y_true, y_pred):
    """
    Intersection over Union (IoU) Loss.
    Lebih baik dari MSE murni untuk bounding box regression.
    """
    # Koordinat intersection
    x1 = tf.maximum(y_true[:, 0], y_pred[:, 0])
    y1 = tf.maximum(y_true[:, 1], y_pred[:, 1])
    x2 = tf.minimum(y_true[:, 2], y_pred[:, 2])
    y2 = tf.minimum(y_true[:, 3], y_pred[:, 3])

    inter_w = tf.maximum(0.0, x2 - x1)
    inter_h = tf.maximum(0.0, y2 - y1)
    intersection = inter_w * inter_h

    area_true = (y_true[:, 2] - y_true[:, 0]) * (y_true[:, 3] - y_true[:, 1])
    area_pred = (y_pred[:, 2] - y_pred[:, 0]) * (y_pred[:, 3] - y_pred[:, 1])
    union = area_true + area_pred - intersection + 1e-7

    iou  = intersection / union
    return 1.0 - tf.reduce_mean(iou)


def combined_loss(y_true, y_pred):
    """IoU Loss + MSE untuk stabilitas training awal."""
    return iou_loss(y_true, y_pred) + tf.keras.losses.MSE(y_true, y_pred)


def mean_iou(y_true, y_pred):
    """Metric: rata-rata IoU (semakin tinggi semakin baik)."""
    x1 = tf.maximum(y_true[:, 0], y_pred[:, 0])
    y1 = tf.maximum(y_true[:, 1], y_pred[:, 1])
    x2 = tf.minimum(y_true[:, 2], y_pred[:, 2])
    y2 = tf.minimum(y_true[:, 3], y_pred[:, 3])

    inter = tf.maximum(0.0, x2 - x1) * tf.maximum(0.0, y2 - y1)
    area_true = (y_true[:, 2] - y_true[:, 0]) * (y_true[:, 3] - y_true[:, 1])
    area_pred = (y_pred[:, 2] - y_pred[:, 0]) * (y_pred[:, 3] - y_pred[:, 1])
    union = area_true + area_pred - inter + 1e-7
    return tf.reduce_mean(inter / union)


# ─────────────────────────────────────────────
# 7. KOMPILASI MODEL
# ─────────────────────────────────────────────
model.compile(
    optimizer=optimizers.Adam(learning_rate=LR),
    loss=combined_loss,
    metrics=[mean_iou, 'mae']
)


# ─────────────────────────────────────────────
# 8. CALLBACKS
# ─────────────────────────────────────────────
cb_list = [
    # Simpan model terbaik berdasarkan val mean_iou
    callbacks.ModelCheckpoint(
        'best_person_detector.keras',
        monitor='val_mean_iou',
        mode='max',
        save_best_only=True,
        verbose=1
    ),
    # Hentikan training jika val_mean_iou tidak membaik
    callbacks.EarlyStopping(
        monitor='val_mean_iou',
        mode='max',
        patience=8,
        restore_best_weights=True,
        verbose=1
    ),
    # Kurangi learning rate jika stagnan
    callbacks.ReduceLROnPlateau(
        monitor='val_loss',
        factor=0.5,
        patience=4,
        min_lr=1e-7,
        verbose=1
    ),
    # Log training ke TensorBoard (opsional)
    callbacks.TensorBoard(log_dir='./logs', histogram_freq=1)
]


# ─────────────────────────────────────────────
# 9. TRAINING
# ─────────────────────────────────────────────
print("\n🚀 Mulai training...\n")
history = model.fit(
    ds_train,
    epochs=EPOCHS,
    validation_data=ds_valid,
    callbacks=cb_list,
    verbose=1
)

# Simpan model final
model.save('final_person_detector.keras')
print("\n✅ Model disimpan: final_person_detector.keras")


# ─────────────────────────────────────────────
# 10. PLOT TRAINING HISTORY
# ─────────────────────────────────────────────
def plot_history(history):
    fig, axes = plt.subplots(1, 3, figsize=(18, 5))

    # Loss
    axes[0].plot(history.history['loss'],     label='Train Loss')
    axes[0].plot(history.history['val_loss'], label='Val Loss')
    axes[0].set_title('Loss (IoU + MSE)')
    axes[0].set_xlabel('Epoch')
    axes[0].legend()
    axes[0].grid(True)

    # Mean IoU
    axes[1].plot(history.history['mean_iou'],     label='Train IoU')
    axes[1].plot(history.history['val_mean_iou'], label='Val IoU')
    axes[1].set_title('Mean IoU (target: > 0.5)')
    axes[1].set_xlabel('Epoch')
    axes[1].legend()
    axes[1].grid(True)

    # MAE
    axes[2].plot(history.history['mae'],     label='Train MAE')
    axes[2].plot(history.history['val_mae'], label='Val MAE')
    axes[2].set_title('MAE Koordinat BBox')
    axes[2].set_xlabel('Epoch')
    axes[2].legend()
    axes[2].grid(True)

    plt.tight_layout()
    plt.savefig('training_history.png', dpi=150)
    plt.show()
    print("Plot disimpan: training_history.png")

plot_history(history)


# ─────────────────────────────────────────────
# 11. EVALUASI PADA DATA TEST
# ─────────────────────────────────────────────
print("\n📊 Evaluasi pada data TEST:")
test_loss, test_iou, test_mae = model.evaluate(ds_test, verbose=0)
print(f"  Test Loss    : {test_loss:.4f}")
print(f"  Test Mean IoU: {test_iou:.4f}  {'✅ Bagus' if test_iou > 0.5 else '⚠️ Perlu peningkatan'}")
print(f"  Test MAE     : {test_mae:.4f}")


# ─────────────────────────────────────────────
# 12. VISUALISASI PREDIKSI vs GROUND TRUTH
# ─────────────────────────────────────────────
def denorm_box(box, w, h):
    """Kembalikan koordinat dari [0,1] ke piksel."""
    xmin = int(box[0] * w)
    ymin = int(box[1] * h)
    xmax = int(box[2] * w)
    ymax = int(box[3] * h)
    return xmin, ymin, xmax, ymax


def visualize_predictions(df, img_dir, model, n=6):
    """Tampilkan n gambar dengan bounding box prediksi vs ground truth."""
    sample = df.sample(n=n, random_state=42).reset_index(drop=True)

    fig, axes = plt.subplots(2, 3, figsize=(15, 10))
    axes = axes.flatten()

    for i, row in sample.iterrows():
        img_path = os.path.join(img_dir, row['filename'])
        img_bgr  = cv2.imread(img_path)
        if img_bgr is None:
            print(f"  Gambar tidak ditemukan: {img_path}")
            continue
        img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
        H, W    = img_rgb.shape[:2]

        # Preprocessing untuk prediksi
        img_input = cv2.resize(img_rgb, (IMG_SIZE, IMG_SIZE)) / 255.0
        img_input = np.expand_dims(img_input, 0).astype('float32')

        pred_box = model.predict(img_input, verbose=0)[0]

        # Konversi koordinat ke piksel asli
        gt_box   = denorm_box([row['xmin_n'], row['ymin_n'],
                                row['xmax_n'], row['ymax_n']], W, H)
        pred_box_px = denorm_box(pred_box, W, H)

        # Gambar bounding box
        ax = axes[i]
        ax.imshow(img_rgb)

        # Ground truth: HIJAU
        gt_rect = patches.Rectangle(
            (gt_box[0], gt_box[1]),
            gt_box[2] - gt_box[0], gt_box[3] - gt_box[1],
            linewidth=2, edgecolor='lime', facecolor='none'
        )
        ax.add_patch(gt_rect)
        ax.text(gt_box[0], gt_box[1] - 5, 'Ground Truth',
                color='lime', fontsize=8, fontweight='bold')

        # Prediksi: MERAH
        pr_rect = patches.Rectangle(
            (pred_box_px[0], pred_box_px[1]),
            pred_box_px[2] - pred_box_px[0], pred_box_px[3] - pred_box_px[1],
            linewidth=2, edgecolor='red', facecolor='none'
        )
        ax.add_patch(pr_rect)
        ax.text(pred_box_px[0], pred_box_px[3] + 15, 'Prediksi',
                color='red', fontsize=8, fontweight='bold')

        ax.set_title(row['filename'][:30], fontsize=7)
        ax.axis('off')

    plt.suptitle('Hijau = Ground Truth | Merah = Prediksi', fontsize=13)
    plt.tight_layout()
    plt.savefig('prediksi_visualisasi.png', dpi=150)
    plt.show()
    print("Visualisasi disimpan: prediksi_visualisasi.png")


visualize_predictions(df_test, TEST_DIR, model, n=6)


# ─────────────────────────────────────────────
# 13. PREDIKSI GAMBAR TUNGGAL (OPSIONAL)
# ─────────────────────────────────────────────
def predict_single_image(img_path, model, threshold=0.5):
    """
    Prediksi bounding box untuk 1 gambar.
    threshold: minimum confidence (tidak dipakai di sini karena
               CNN regression tidak menghasilkan confidence score,
               tapi bisa ditambahkan head classifier terpisah).
    """
    img_bgr = cv2.imread(img_path)
    if img_bgr is None:
        print("Gambar tidak ditemukan:", img_path)
        return

    img_rgb   = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
    H, W      = img_rgb.shape[:2]
    img_input = cv2.resize(img_rgb, (IMG_SIZE, IMG_SIZE)) / 255.0
    img_input = np.expand_dims(img_input, 0).astype('float32')

    pred      = model.predict(img_input, verbose=0)[0]
    xmin, ymin, xmax, ymax = denorm_box(pred, W, H)

    print(f"  Prediksi BBox: xmin={xmin}, ymin={ymin}, xmax={xmax}, ymax={ymax}")

    fig, ax = plt.subplots(1, figsize=(8, 6))
    ax.imshow(img_rgb)
    rect = patches.Rectangle(
        (xmin, ymin), xmax - xmin, ymax - ymin,
        linewidth=3, edgecolor='red', facecolor='none'
    )
    ax.add_patch(rect)
    ax.text(xmin, ymin - 10, 'Person', color='red',
            fontsize=12, fontweight='bold')
    ax.axis('off')
    plt.title('Deteksi Person')
    plt.tight_layout()
    plt.show()


# Contoh penggunaan:
# predict_single_image('/content/Dataset/test/contoh.jpg', model)