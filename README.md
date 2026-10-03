# 🚁 Drone SAR PPO YOLO Object Detection

A Unity-based Search and Rescue (SAR) drone simulation using Deep Reinforcement Learning with PPO and YOLO for real-time object detection.

## Overview

This project combines Unity-based simulation, reinforcement learning, and computer vision to develop an autonomous SAR drone that can:

- navigate complex environments,
- search for targets efficiently,
- detect objects in real time using YOLO,
- optimize decision-making through PPO training.

The repository is designed for research, experimentation, and simulation-based testing of autonomous drone behavior in search-and-rescue scenarios.

## Features

- Unity simulation environment for SAR drone missions
- PPO-based reinforcement learning agent training
- Real-time object detection with YOLO
- Autonomous navigation and task execution
- Modular pipeline for experimentation and tuning
- Support for simulation analysis and model evaluation

## Tech Stack

- C# for Unity gameplay and simulation logic
- Python for ML pipelines, training, and inference
- YOLO for object detection
- PPO (Proximal Policy Optimization) for reinforcement learning
- Unity Engine for 3D environment and visualization

## Repository Structure

```text
.
├── Assets/
│   ├── Scenes/
│   ├── Scripts/
│   ├── Models/
│   └── Prefabs/
├── Python/
│   ├── training/
│   ├── utils/
│   └── requirements.txt
├── Data/
│   ├── logs/
│   └── datasets/
├── Models/
├── Docs/
├── README.md
├── LICENSE
└── .gitignore
```

## Requirements

- Unity 2021.3 LTS or newer
- Python 3.8+
- CUDA-enabled GPU recommended for training and YOLO inference
- Git LFS (if large assets are used)

## Installation

1. Clone the repository
```bash
git clone https://github.com/Cavela23/drone-sar-ppo-yolo-object-detection.git
cd drone-sar-ppo-yolo-object-detection
```

2. Set up the Python environment
```bash
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate
pip install -r Python/requirements.txt
```

3. Open the project in Unity and install required packages as needed.

## Usage

### Training the PPO agent
```bash
python Python/training/train.py
```

### Running object detection
```bash
python Python/yolo/detect.py --source <input>
```

### Running the Unity simulation
- Open the Unity project
- Load the main simulation scene
- Press Play to run the environment

## Configuration

Project settings and training parameters can be adjusted in configuration files under the project’s training and environment directories. Tune parameters such as:

- reward function
- learning rate
- batch size
- PPO update steps
- detection confidence threshold

## Goals

This project aims to demonstrate a realistic framework for combining:

- autonomous drone control,
- reinforcement learning optimization,
- computer vision-based detection,
- and simulation-driven development in SAR use cases.

## License

This project is licensed under the MIT License. See the `LICENSE` file for details.

## Contributing

Contributions are welcome. To contribute:

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Open a pull request

## Contact

For questions or collaboration opportunities, open an issue in this repository or reach out through the GitHub profile associated with the project owner.
