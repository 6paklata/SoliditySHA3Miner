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

#pragma once

#include <list>
#include <string>
#include <intrin.h>
#include "types.h"
#include "instance.h"
#include "sha3/keccak-tiny.h"
#include "sha3/sha3-midstate.h"

namespace CPUSolver
{
	class CpuSolver
	{
	public:
		static void SHA3(byte32_t *message, byte32_t *digest);
		static void GetCpuName(const char *cpuName);

		void SetThreadAffinity(int affinityMask, const char *errorMessage);
		void HashMessage(Instance *deviceInstance, Processor *processor);
		void HashMidState(Instance *deviceInstance, Processor *processor);

	private:
		bool IslessThan(byte32_t &left, byte32_t &right);
	};
}