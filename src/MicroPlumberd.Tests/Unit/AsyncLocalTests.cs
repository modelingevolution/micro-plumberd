using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;

namespace MicroPlumberd.Tests.Unit
{
    public class AsyncLocalTests
    {
        static AsyncLocal<int> _asyncLocal = new AsyncLocal<int>();

        [Fact]
        public async Task AsyncLocalWorksAsExpected()
        {
            _asyncLocal.Value = 10;
            bool completed = false;
            await Task.Factory.StartNew(async () =>
            {
                _asyncLocal.Value.Should().Be(10);
                _asyncLocal.Value = 20;
                completed = true;
            });
            await Task.Delay(500);
            completed.Should().BeTrue();
            _asyncLocal.Value.Should().Be(10);
        }
    }
}
