﻿using System.Linq;
using FluentAssertions;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Tax;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Events;
using Nop.Services.Logging;
using Nop.Services.Plugins;
using Nop.Services.Tax;
using Nop.Tests;
using NUnit.Framework;

namespace Nop.Services.Tests.Tax
{
    [TestFixture]
    public class TaxServiceTests : ServiceTest
    {
        private Mock<IAddressService> _addressService;
        private IWorkContext _workContext;
        private Mock<IStoreContext> _storeContext;
        private TaxSettings _taxSettings;
        private Mock<IEventPublisher> _eventPublisher;
        private ITaxPluginManager _taxPluginManager;
        private ITaxService _taxService;
        private Mock<IGeoLookupService> _geoLookupService;
        private Mock<ICountryService> _countryService;
        private Mock<IStateProvinceService> _stateProvinceService;
        private Mock<ILogger> _logger;
        private Mock<IWebHelper> _webHelper;
        private CustomerSettings _customerSettings;
        private ShippingSettings _shippingSettings;
        private AddressSettings _addressSettings;
        private Mock<IGenericAttributeService> _genericAttributeService;
        private CatalogSettings _catalogSettings;

        [SetUp]
        public new void SetUp()
        {
            _taxSettings = new TaxSettings
            {
                DefaultTaxAddressId = 10
            };

            _workContext = null;
            _storeContext = new Mock<IStoreContext>();
            _storeContext.Setup(x => x.CurrentStore).Returns(new Store { Id = 1 });

            _addressService = new Mock<IAddressService>();
            //default tax address
            _addressService.Setup(x => x.GetAddressById(_taxSettings.DefaultTaxAddressId)).Returns(new Address { Id = _taxSettings.DefaultTaxAddressId });

            _eventPublisher = new Mock<IEventPublisher>();
            _eventPublisher.Setup(x => x.Publish(It.IsAny<object>()));

            _geoLookupService = new Mock<IGeoLookupService>();
            _countryService = new Mock<ICountryService>();
            _stateProvinceService = new Mock<IStateProvinceService>();
            _logger = new Mock<ILogger>();
            _webHelper = new Mock<IWebHelper>();
            _genericAttributeService = new Mock<IGenericAttributeService>();

            _customerSettings = new CustomerSettings();
            _shippingSettings = new ShippingSettings();
            _addressSettings = new AddressSettings();

            var customerService = new Mock<ICustomerService>();
            var loger = new Mock<ILogger>();

            _catalogSettings = new CatalogSettings();
            var pluginService = new PluginService(_catalogSettings, customerService.Object, loger.Object, CommonHelper.DefaultFileProvider, _webHelper.Object);
            _taxPluginManager = new TaxPluginManager(pluginService, _taxSettings);

            var cacheManager = new TestCacheManager();

            _taxService = new TaxService(_addressSettings,
                _customerSettings,
                _addressService.Object,
                _countryService.Object,
                _genericAttributeService.Object,
                _geoLookupService.Object,
                _logger.Object,
                _stateProvinceService.Object,
                cacheManager,
                _storeContext.Object,
                _taxPluginManager,
                _webHelper.Object,
                _workContext,
                _shippingSettings,
                _taxSettings);
        }

        [Test]
        public void Can_load_taxProviders()
        {
            var providers = _taxPluginManager.LoadAllPlugins();
            providers.Should().NotBeNull();
            providers.Any().Should().BeTrue();
        }

        [Test]
        public void Can_load_taxProvider_by_systemKeyword()
        {
            var provider = _taxPluginManager.LoadPluginBySystemName("FixedTaxRateTest");
            provider.Should().NotBeNull();
        }

        [Test]
        public void Can_load_active_taxProvider()
        {
            var provider = _taxPluginManager.LoadPrimaryPlugin();
            provider.Should().NotBeNull();
        }

        [Test]
        public void Can_check_taxExempt_product()
        {
            var product = new Product
            {
                IsTaxExempt = true
            };
            _taxService.IsTaxExempt(product, null).Should().BeTrue();
            product.IsTaxExempt = false;
            _taxService.IsTaxExempt(product, null).Should().BeFalse();
        }

        [Test]
        public void Can_check_taxExempt_customer()
        {
            var customer = new Customer
            {
                IsTaxExempt = true
            };
            _taxService.IsTaxExempt(null, customer).Should().BeTrue();
            customer.IsTaxExempt = false;
            _taxService.IsTaxExempt(null, customer).Should().BeFalse();
        }

        [Test]
        public void Can_check_taxExempt_customer_in_taxExemptCustomerRole()
        {
            var customer = new Customer
            {
                IsTaxExempt = false
            };
            _taxService.IsTaxExempt(null, customer).Should().BeFalse();

            var customerRole = new CustomerRole
            {
                TaxExempt = true,
                Active = true
            };
            customer.CustomerRoles.Add(customerRole);
            _taxService.IsTaxExempt(null, customer).Should().BeTrue();
            customerRole.TaxExempt = false;
            _taxService.IsTaxExempt(null, customer).Should().BeFalse();

            //if role is not active, we should ignore 'TaxExempt' property
            customerRole.Active = false;
            _taxService.IsTaxExempt(null, customer).Should().BeFalse();
        }

        [Test]
        public void Can_get_productPrice_priceIncludesTax_includingTax_taxable()
        {
            var customer = new Customer();
            var product = new Product();

            _taxService.GetProductPrice(product, 0, 1000M, true, customer, true, out _).Should().Be(1000);
            _taxService.GetProductPrice(product, 0, 1000M, true, customer, false, out _).Should().Be(1100);
            _taxService.GetProductPrice(product, 0, 1000M, false, customer, true, out _).Should().Be(909.0909090909090909090909091M);
            _taxService.GetProductPrice(product, 0, 1000M, false, customer, false, out _).Should().Be(1000);
        }

        [Test]
        public void Can_get_productPrice_priceIncludesTax_includingTax_non_taxable()
        {
            var customer = new Customer();
            var product = new Product();

            //not taxable
            customer.IsTaxExempt = true;

            _taxService.GetProductPrice(product, 0, 1000M, true, customer, true, out _).Should().Be(909.0909090909090909090909091M);
            _taxService.GetProductPrice(product, 0, 1000M, true, customer, false, out _).Should().Be(1000);
            _taxService.GetProductPrice(product, 0, 1000M, false, customer, true, out _).Should().Be(909.0909090909090909090909091M);
            _taxService.GetProductPrice(product, 0, 1000M, false, customer, false, out _).Should().Be(1000);
        }

        [Test]
        public void Can_do_VAT_check()
        {
            var vatNumberStatus1 = _taxService.DoVatCheck("GB", "523 2392 69",
                out _, out _, out var exception);

            if (exception != null)
            {
                TestContext.WriteLine($"Can't run the \"Can_do_VAT_check\":\r\n{exception.Message}");
                return;
            }

            vatNumberStatus1.Should().Be(VatNumberStatus.Valid);

            var vatNumberStatus2 = _taxService.DoVatCheck("GB", "000 0000 00",
                out _, out _, out exception);

            if (exception != null)
            {
                TestContext.WriteLine($"Can't run the \"Can_do_VAT_check\":\r\n{exception.Message}");
                return;
            }

            vatNumberStatus2.Should().Be(VatNumberStatus.Invalid);
        }

        [Test]
        public void Should_assume_valid_VAT_number_if_EuVatAssumeValid_setting_is_true()
        {
            _taxSettings.EuVatAssumeValid = true;

            var vatNumberStatus = _taxService.GetVatNumberStatus("GB", "000 0000 00", out _, out _);
            vatNumberStatus.Should().Be(VatNumberStatus.Valid);
        }
    }
}
