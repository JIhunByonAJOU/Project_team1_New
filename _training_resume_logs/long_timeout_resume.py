import os
import sys
from multiprocessing import freeze_support

from mlagents.trainers import learn
from mlagents_envs.environment import UnityEnvironment


def create_environment_factory(
    env_path,
    no_graphics,
    seed,
    start_port,
    env_args,
    log_folder,
):
    timeout_wait = int(os.environ.get("MLAGENTS_TIMEOUT_WAIT_SECONDS", "3600"))

    def create_unity_environment(worker_id, side_channels):
        return UnityEnvironment(
            file_name=env_path,
            worker_id=worker_id,
            seed=seed + worker_id,
            no_graphics=no_graphics,
            base_port=start_port,
            additional_args=env_args,
            side_channels=side_channels,
            log_folder=log_folder,
            timeout_wait=timeout_wait,
        )

    return create_unity_environment


if __name__ == "__main__":
    freeze_support()
    learn.create_environment_factory = create_environment_factory
    learn.run_cli(learn.parse_command_line(sys.argv[1:]))
