﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace il2cpp
{
	// 指令
	internal class InstInfo
	{
		public OpCode OpCode;
		public object Operand;
		public int Offset;

		public bool IsBrTarget;
		public bool IsGenerated;
		public string InstCode;
	}

	internal enum StackTypeKind
	{
		I4,
		I8,
		R4,
		R8,
		Ptr,
		Ref,
		Obj,
		ValueType
	}

	// 栈类型
	internal struct StackType
	{
		public readonly string TypeName;
		public readonly StackTypeKind Kind;

		public static StackType I4 = new StackType(StackTypeKind.I4);
		public static StackType I8 = new StackType(StackTypeKind.I8);
		public static StackType R4 = new StackType(StackTypeKind.R4);
		public static StackType R8 = new StackType(StackTypeKind.R8);
		public static StackType Ptr = new StackType(StackTypeKind.Ptr);
		public static StackType Ref = new StackType(StackTypeKind.Ref);
		public static StackType Obj = new StackType(StackTypeKind.Obj);

		public StackType(StackTypeKind kind)
		{
			Debug.Assert(kind != StackTypeKind.ValueType);
			TypeName = null;
			Kind = kind;
		}

		public StackType(string typeName)
		{
			TypeName = typeName;
			Kind = StackTypeKind.ValueType;
		}

		public string GetPostfix()
		{
			switch (Kind)
			{
				case StackTypeKind.I4:
					return "i4";
				case StackTypeKind.I8:
					return "i8";
				case StackTypeKind.R4:
					return "r4";
				case StackTypeKind.R8:
					return "r8";
				case StackTypeKind.Ptr:
					return "ptr";
				case StackTypeKind.Ref:
					return "ref";
				case StackTypeKind.Obj:
					return "obj";
				case StackTypeKind.ValueType:
					return TypeName;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public string GetTypeName()
		{
			switch (Kind)
			{
				case StackTypeKind.I4:
					return "int32_t";
				case StackTypeKind.I8:
					return "int64_t";
				case StackTypeKind.R4:
					return "float";
				case StackTypeKind.R8:
					return "double";
				case StackTypeKind.Ptr:
					return "void*";
				case StackTypeKind.Ref:
					return "void*";
				case StackTypeKind.Obj:
					return "cls_Object*";
				case StackTypeKind.ValueType:
					return TypeName;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}

	// 栈槽
	internal class SlotInfo
	{
		public readonly StackType SlotType;
		public readonly int SlotIndex;

		public SlotInfo(StackType stype, int idx)
		{
			SlotType = stype;
			SlotIndex = idx;
		}
	}

	// 方法生成器
	internal class MethodGenerator
	{
		private readonly GeneratorContext GenContext;
		private readonly MethodX CurrMethod;

		// 类型栈
		private Stack<StackType> TypeStack = new Stack<StackType>();
		// 分支队列
		private readonly Queue<Tuple<Stack<StackType>, int>> Branches = new Queue<Tuple<Stack<StackType>, int>>();

		private int PushCount = 0;
		private int PopCount = 0;

		public MethodGenerator(GeneratorContext genContext, MethodX metX)
		{
			GenContext = genContext;
			CurrMethod = metX;
		}

		private SlotInfo Push(StackType stype)
		{
			SlotInfo slot = new SlotInfo(stype, TypeStack.Count);
			TypeStack.Push(stype);
			++PushCount;
			return slot;
		}

		private SlotInfo Pop()
		{
			Debug.Assert(TypeStack.Count > 0);
			StackType stype = TypeStack.Pop();
			++PopCount;
			return new SlotInfo(stype, TypeStack.Count);
		}

		private SlotInfo[] Pop(int num)
		{
			SlotInfo[] slots = new SlotInfo[num];
			for (int i = num - 1; i >= 0; --i)
				slots[i] = Pop();
			return slots;
		}

		private SlotInfo Peek()
		{
			Debug.Assert(TypeStack.Count > 0);
			return new SlotInfo(TypeStack.Peek(), TypeStack.Count - 1);
		}

		private void AddBranch(int target)
		{
			Branches.Enqueue(new Tuple<Stack<StackType>, int>(
				new Stack<StackType>(TypeStack),
				target));
		}

		public void Generate()
		{
			var instList = CurrMethod.InstList;
			if (instList != null)
			{
				int currIP = 0;
				for (; ; )
				{
					if (!GenerateInst(instList[currIP], ref currIP))
					{
						if (Branches.Count > 0)
						{
							var branch = Branches.Dequeue();
							TypeStack = branch.Item1;
							currIP = branch.Item2;
						}
						else
							break;
					}
				}
			}
		}

		private bool GenerateInst(InstInfo inst, ref int currIP)
		{
			if (inst.IsGenerated)
				return false;
			inst.IsGenerated = true;

			GenerateInstCode(inst);

			var opCode = inst.OpCode;
			var operand = inst.Operand;

			switch (opCode.StackBehaviourPop)
			{ }

			switch (opCode.StackBehaviourPush)
			{ }

			PushCount = PopCount = 0;

			switch (opCode.FlowControl)
			{
				case FlowControl.Branch:
					currIP = (int)inst.Operand;
					return true;

				case FlowControl.Cond_Branch:
					if (opCode.Code == Code.Switch)
					{
						Debug.Assert(operand is int[]);
						int[] targets = (int[])operand;
						foreach (int target in targets)
							AddBranch(target);
					}
					else
					{
						Debug.Assert(operand is int);
						AddBranch((int)operand);
					}
					++currIP;
					return true;

				case FlowControl.Break:
				case FlowControl.Call:
				case FlowControl.Meta:
				case FlowControl.Next:
					++currIP;
					return true;

				case FlowControl.Return:
				case FlowControl.Throw:
					return false;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void GenerateInstCode(InstInfo inst)
		{
			var opCode = inst.OpCode;
			var operand = inst.Operand;

			switch (opCode.Code)
			{
				case Code.Nop:
					return;

				case Code.Pop:
					Pop();
					return;

				case Code.Dup:
					GenDup(inst);
					return;

				case Code.Ldarg_0:
					GenLdarg(inst, 0);
					return;
				case Code.Ldarg_1:
					GenLdarg(inst, 1);
					return;
				case Code.Ldarg_2:
					GenLdarg(inst, 2);
					return;
				case Code.Ldarg_3:
					GenLdarg(inst, 3);
					return;

				case Code.Ldarg:
				case Code.Ldarg_S:
					GenLdarg(inst, ((Parameter)operand).Index);
					return;

				case Code.Ldarga:
				case Code.Ldarga_S:
					GenLdarg(inst, ((Parameter)operand).Index, true);
					return;

				case Code.Starg:
				case Code.Starg_S:
					GenStarg(inst, ((Parameter)operand).Index);
					return;
			}

			throw new NotImplementedException();
		}

		private void GenDup(InstInfo inst)
		{
			var slotTop = Peek();
			var slotPush = Push(slotTop.SlotType);
			inst.InstCode = GenAssign(TempName(slotPush), TempName(slotTop), null);
		}

		private void GenLdarg(InstInfo inst, int argID, bool isAddr = false)
		{
			Debug.Assert(argID < CurrMethod.ParamTypes.Count);
			var argType = CurrMethod.ParamTypes[argID];
			var slotPush = isAddr ? Push(StackType.Ptr) : Push(ToStackType(argType));
			inst.InstCode = GenAssign(TempName(slotPush), (isAddr ? "&" : null) + ArgName(argID), slotPush.SlotType);
		}

		private void GenStarg(InstInfo inst, int argID)
		{
			Debug.Assert(argID < CurrMethod.ParamTypes.Count);
			var argType = CurrMethod.ParamTypes[argID];
			var slotPop = Pop();
			inst.InstCode = GenAssign(ArgName(argID), TempName(slotPop), ToStackType(argType));
		}

		private StackType ToStackType(TypeSig tySig)
		{
			switch (tySig.ElementType)
			{
				case ElementType.I1:
				case ElementType.I2:
				case ElementType.I4:
				case ElementType.U1:
				case ElementType.U2:
				case ElementType.U4:
				case ElementType.Boolean:
				case ElementType.Char:
					return StackType.I4;

				case ElementType.I8:
				case ElementType.U8:
					return StackType.I8;

				case ElementType.R4:
					return StackType.R4;

				case ElementType.R8:
					return StackType.R8;

				case ElementType.I:
				case ElementType.U:
				case ElementType.Ptr:
					return StackType.Ptr;

				case ElementType.ByRef:
					return StackType.Ref;
			}

			if (tySig.IsValueType)
				return new StackType(GenContext.GetTypeName(tySig));

			return StackType.Obj;
		}

		private static string GenAssign(string lhs, string rhs, StackType? stype)
		{
			return lhs + " = " + (stype != null ? '(' + stype.Value.GetTypeName() + ')' : null) + rhs + ';';
		}

		private static string ArgName(int argID)
		{
			return "arg_" + argID;
		}

		private static string LocalName(int locID)
		{
			return "loc_" + locID;
		}

		private static string TempName(SlotInfo slot)
		{
			return "tmp_" + slot.SlotIndex + '_' + slot.SlotType.GetPostfix();
		}
	}
}
