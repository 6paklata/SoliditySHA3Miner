/*
   Copyright 2018 Lip Wee Yeo Amano

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

	   http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

#include "cpuSolver.h"

namespace CPUSolver
{
	void CpuSolver::SHA3(byte32_t *message, byte32_t *digest)
	{
		keccak_256(&(*digest)[0], UINT256_LENGTH, &(*message)[0], MESSAGE_LENGTH);
	}

	void CpuSolver::GetCpuName(const char *cpuName)
	{
		int info[4];
		__cpuidex(info, 0x80000000, 0);

		if (info[0] < 0x80000004) return;

		int x = 0;
		for (uint32_t i{ 0x80000002 }; i < 0x80000005; ++i, x += 16)
		{
			__cpuidex(info, i, 0);
			std::memcpy((void *)&cpuName[x], (void *)info, 16);
		}
		std::memset((void *)&cpuName[x + 1], 0, 1);
	}

#ifdef __linux__

#include <sched.h>

	void CpuSolver::SetThreadAffinity(int affinityMask, const char *errorMessage)
	{
		cpu_set_t mask_set{ 0 };
		CPU_SET(affinityMask, &mask_set);

		if (sched_setaffinity(0, sizeof(cpu_set_t), &mask_set) != 0)
		{
			auto errMessage = "Failed to set processor affinity (" + std::to_string(affinityMask) + ")";
			auto errMessageChar = errMessage.c_str();
			std::memcpy((void *)errorMessage, errMessageChar, errMessage.length());
			std::memset((void *)&errorMessage[errMessage.length()], 0, 1);
		}
	}

#else

#include <Windows.h>

	void CpuSolver::SetThreadAffinity(int affinityMask, const char *errorMessage)
	{
		if (!SetThreadAffinityMask(GetCurrentThread(), 1 << affinityMask))
		{
			auto errMessage = "Failed to set processor affinity (" + std::to_string(affinityMask) + ")";
			auto errMessageChar = errMessage.c_str();
			std::memcpy((void *)errorMessage, errMessageChar, errMessage.length());
			std::memset((void *)&errorMessage[errMessage.length()], 0, 1);
		}
	}

#endif

	void CpuSolver::HashMessage(Instance *deviceInstance, Processor *processor)
	{
		byte32_t digest;
		byte32_t currentTarget;
		byte32_t currentSolution;
		message_ut currentMessage;

		std::memcpy(&currentMessage, deviceInstance->Message, MESSAGE_LENGTH);
		std::memcpy(&currentTarget, deviceInstance->Target, UINT256_LENGTH);
		std::memcpy(&currentSolution, deviceInstance->SolutionTemplate, UINT256_LENGTH);

		auto endWorkPosition = processor->WorkPosition + processor->WorkSize;
		auto maxSolutionCount = deviceInstance->MaxSolutionCount;

		for (auto i = processor->WorkPosition; i < endWorkPosition; ++i)
		{
			std::memcpy(&currentSolution[ADDRESS_LENGTH], &i, UINT64_LENGTH);
			currentMessage.structure.solution = currentSolution;

			keccak_256(&(digest)[0], UINT256_LENGTH, &currentMessage.byteArray[0], MESSAGE_LENGTH);

			if (IslessThan(digest, currentTarget))
			{
				if (deviceInstance->SolutionCount < maxSolutionCount)
				{
					deviceInstance->Solutions[deviceInstance->SolutionCount] = i;
					deviceInstance->SolutionCount++;
				}
			}
		}
	}

	void CpuSolver::HashMidState(Instance *deviceInstance, Processor *processor)
	{
		auto endWorkPosition = processor->WorkPosition + processor->WorkSize;
		auto currentHigh64Target = *deviceInstance->High64Target;
		auto maxSolutionCount = deviceInstance->MaxSolutionCount;

		uint64_t currentMidState[SPONGE_LENGTH / UINT64_LENGTH];
		std::memcpy(&currentMidState, deviceInstance->MidState, SPONGE_LENGTH);

		for (auto currentWorkPosition = processor->WorkPosition; currentWorkPosition < endWorkPosition; ++currentWorkPosition)
		{
			sha3_midstate(currentMidState, currentHigh64Target, currentWorkPosition, maxSolutionCount,
				&deviceInstance->SolutionCount, deviceInstance->Solutions);
		}
	}

	bool CpuSolver::IslessThan(byte32_t &left, byte32_t &right)
	{
		for (uint32_t i{ 0 }; i < UINT256_LENGTH; ++i)
		{
			if (left[i] < right[i]) return true;
			else if (left[i] > right[i]) return false;
		}
		return false;
	}
}