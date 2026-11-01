using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MiniAI.Portable {
    internal static class PayloadCodec {
        [DllImport("cabinet.dll",SetLastError=true)]static extern bool CreateDecompressor(uint algorithm,IntPtr allocation,out IntPtr handle);
        [DllImport("cabinet.dll",SetLastError=true)]static extern bool Decompress(IntPtr handle,byte[] input,UIntPtr inputSize,byte[] output,UIntPtr outputSize,out UIntPtr written);
        [DllImport("cabinet.dll")]static extern bool CloseDecompressor(IntPtr handle);
        internal static void Decode(Stream stream,long expected,bool shuffle,Action<byte[],int> write){using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true)){
            if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))!="MAIPK01\0"||reader.ReadUInt32()!=(shuffle?1u:0u)||reader.ReadInt64()!=expected)throw new IOException("Invalid compact payload header.");
            IntPtr handle;if(!CreateDecompressor(5,IntPtr.Zero,out handle))throw new Win32Exception(Marshal.GetLastWin32Error());long total=0;
            try{while(total<expected){uint size=reader.ReadUInt32(),compressed=reader.ReadUInt32();if(size==0||size>8*1024*1024||compressed==0||compressed>16*1024*1024||total+size>expected)throw new IOException("Invalid compact payload block.");byte[] input=reader.ReadBytes((int)compressed);if(input.Length!=compressed)throw new EndOfStreamException();var output=new byte[(int)size];UIntPtr written;if(!Decompress(handle,input,new UIntPtr(compressed),output,new UIntPtr(size),out written)||written.ToUInt64()!=size)throw new IOException("Cannot decompress the offline payload.");
                if(shuffle){var restored=new byte[output.Length];int count=output.Length/4,end=count*4;for(int lane=0;lane<4;lane++)for(int i=0;i<count;i++)restored[i*4+lane]=output[lane*count+i];Buffer.BlockCopy(output,end,restored,end,output.Length-end);output=restored;}write(output,output.Length);total+=size;
            }if(reader.Read()!=-1)throw new IOException("Unexpected compact payload data.");}finally{CloseDecompressor(handle);}
        }}
    }
}
